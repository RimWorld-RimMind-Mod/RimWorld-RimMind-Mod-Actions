using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    internal static class ActionHelper
    {
        internal static IntVec3? ParseCell(string s)
        {
            var parts = s.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), out int x) &&
                int.TryParse(parts[1].Trim(), out int z))
                return new IntVec3(x, 0, z);
            return null;
        }
    }

    // ─────────────────────────────────────────────
    //  force_rest
    // ─────────────────────────────────────────────
    public class ForceRestAction : IActionRule
    {
        public string IntentId => "force_rest";
        public string DisplayName => "RimMind.Actions.DisplayName.ForceRest".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Optional bed location as @x,z coordinates\"}},\"required\":[]}";

        /// <summary>
        /// param（可选）：
        ///   "@x,z"  — 指定坐标处的床（AI 可让小人去自己的床而非随机医疗床）
        /// target（可选）：
        ///   Pawn    — 去该 Pawn 的指定床（如医生安排伤员去特定床位）
        /// 均未提供时：用 RestUtility.FindBedFor 自动寻找最合适的床。
        /// </summary>
        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.Dead || actor.Downed || actor.Map == null) return false;

            Building_Bed? bed = null;

            // 优先：param 指定坐标 "@x,z"
            if (!string.IsNullOrEmpty(param) && param!.StartsWith("@"))
            {
                var cell = ActionHelper.ParseCell(param.Substring(1));
                if (cell.HasValue && cell.Value.InBounds(actor.Map))
                    bed = actor.Map.thingGrid.ThingsListAt(cell.Value)
                              .OfType<Building_Bed>()
                              .FirstOrDefault();
                if (bed == null)
                    Log.Warning($"[RimMind-Actions] force_rest: no bed found at {param}, falling back to auto-find");
            }

            // 次选：target Pawn 的床
            if (bed == null && target?.ownership?.OwnedBed != null)
                bed = target.ownership.OwnedBed;

            // 兜底：自动寻找
            if (bed == null)
                bed = RestUtility.FindBedFor(actor, actor, false, false);

            Job job = bed != null
                ? JobMaker.MakeJob(JobDefOf.LayDown, bed)
                : JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);

            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing);
            return true;
        }

    }

    // ─────────────────────────────────────────────
    //  assign_work
    // ─────────────────────────────────────────────
    /// <summary>
    /// 描述一个可执行的工作目标，供 Advisor 构建候选 prompt 用。
    /// </summary>
    public class WorkTargetInfo
    {
        /// <summary>AI 可读的目标名称，如"花岗岩"</summary>
        public string Label = "";
        /// <summary>Thing.def.defName，如"Granite"</summary>
        public string DefName = "";
        /// <summary>地图坐标，用于 assign_work param 编码</summary>
        public IntVec3 Position;
        /// <summary>到小人的距离（格），便于 Advisor 排序展示</summary>
        public float Distance;

        /// <summary>生成 assign_work 的 param 字符串，格式 "WorkType@x,z"</summary>
        public string ToParam(string workTypeDefName)
            => $"{workTypeDefName}@{Position.x},{Position.z}";

        public override string ToString()
            => "RimMind.Actions.Prompt.WorkTargetInfo".Translate(Label, DefName, $"{Position.x}", $"{Position.z}", $"{Distance:F0}");
    }

    public class AssignWorkAction : IActionRule
    {
        public string IntentId => "assign_work";
        public string DisplayName => "RimMind.Actions.DisplayName.AssignWork".Translate();
        public RiskLevel RiskLevel => RiskLevel.Low;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Work type name (e.g. Mining) or WorkType@x,z for specific target\"}},\"required\":[\"param\"]}";

        /// <summary>
        /// param 支持两种格式：
        ///   "Mining"        — 让 WorkGiver 自动选最优目标（原有行为）
        ///   "Mining@45,32"  — AI 指定地图坐标 (45,32) 处的具体目标
        /// </summary>
        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;
            if (actor.Map == null) return false;

            // 解析 param：拆分 workTypeName 和可选的 @x,z
            string workTypeName;
            IntVec3? forcedCell = null;

            int atIdx = param!.IndexOf('@');
            if (atIdx >= 0)
            {
                workTypeName = param.Substring(0, atIdx);
                var cellPart = param.Substring(atIdx + 1);
                forcedCell = ActionHelper.ParseCell(cellPart);
                if (forcedCell == null)
                {
                    Log.Warning($"[RimMind-Actions] assign_work: cannot parse cell '{cellPart}', falling back to auto-target");
                }
            }
            else
            {
                workTypeName = param;
            }

            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeName);
            if (workType == null)
            {
                Log.Warning($"[RimMind-Actions] assign_work: unknown WorkTypeDef '{workTypeName}'");
                return false;
            }
            if (actor.workSettings == null || actor.workSettings.GetPriority(workType) <= 0)
                return false;

            // 指定坐标：直接找对应 Thing 并生成 Job
            if (forcedCell.HasValue)
                return ExecuteAtCell(actor, workType, forcedCell.Value, requestQueueing);

            // 自动选目标：取 WorkGiver 列表第一个有效目标
            return ExecuteAuto(actor, workType, requestQueueing);
        }

        private static bool ExecuteAtCell(Pawn actor, WorkTypeDef workType,
            IntVec3 cell, bool requestQueueing)
        {
            try
            {
                if (!cell.InBounds(actor.Map))
                {
                    Log.Warning($"[RimMind-Actions] assign_work: cell {cell} out of map bounds");
                    return false;
                }

                foreach (var wg in workType.workGiversByPriority)
                {
                    if (wg.Worker is not WorkGiver_Scanner scanner) continue;
                    if (scanner.ShouldSkip(actor, false)) continue;

                    var thingsAt = actor.Map.thingGrid.ThingsListAt(cell);
                    foreach (var thing in thingsAt)
                    {
                        if (!scanner.HasJobOnThing(actor, thing, false)) continue;
                        var job = scanner.JobOnThing(actor, thing, false);
                        if (job == null) continue;
                        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing);
                        return true;
                    }

                    if (scanner.HasJobOnCell(actor, cell, false))
                    {
                        var job = scanner.JobOnCell(actor, cell, false);
                        if (job != null)
                        {
                            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing);
                            return true;
                        }
                    }
                }

                Log.Warning($"[RimMind-Actions] assign_work: no {workType.defName} work target found at cell {cell}");
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimMind-Actions] ExecuteAtCell: {workType.defName} threw at {cell}: {e.Message}");
                return false;
            }
        }

        private static bool ExecuteAuto(Pawn actor, WorkTypeDef workType, bool requestQueueing)
        {
            foreach (var wg in workType.workGiversByPriority)
            {
                if (wg.Worker is not WorkGiver_Scanner scanner) continue;
                if (scanner.ShouldSkip(actor, false)) continue;

                var things = scanner.PotentialWorkThingsGlobal(actor);
                if (things == null) continue;

                foreach (var thing in things)
                {
                    try
                    {
                        var job = scanner.JobOnThing(actor, thing, false);
                        if (job == null) continue;
                        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[RimMind-Actions] ExecuteAuto: {wg.defName} threw for {thing}: {e.Message}");
                    }
                }
            }
            return false;
        }

        // ── 静态工具：枚举可用工作目标（供 Advisor 调用）──────────────────────

        /// <summary>
        /// 枚举小人对指定工作类型可执行的目标列表，按距离排序，限制数量。
        ///
        /// <para>支持三类 WorkGiver：</para>
        /// <list type="bullet">
        ///   <item>Thing-based（Mining/Hauling/Tend/Hunt 等）：返回具体 Thing</item>
        ///   <item>DoBill-based（Cooking/Crafting/Art/Smithing 等）：返回工作台 + 账单名称</item>
        ///   <item>Cell-based（Growing 种植等）：返回地块坐标 + 区域名称</item>
        /// </list>
        /// </summary>
        public static List<WorkTargetInfo> GetWorkTargets(
            Pawn pawn, string workTypeDefName, int maxCount = 8)
        {
            var result = new List<WorkTargetInfo>();

            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null) return result;
            if (pawn.workSettings == null || pawn.workSettings.GetPriority(workType) <= 0)
                return result;

            int cap = maxCount * 3; // 收集上限（排序前），防止大地图扫描过久
            var seenThings = new HashSet<Thing>();
            var seenCells = new HashSet<IntVec3>();

            foreach (var wg in workType.workGiversByPriority)
            {
                if (wg.Worker is not WorkGiver_Scanner scanner) continue;
                if (scanner.ShouldSkip(pawn, false)) continue;

                // ── Cell-based WorkGiver（Growing 等）─────────────────────────
                if (wg.scanCells)
                {
                    IEnumerable<IntVec3>? cells = null;
                    try { cells = scanner.PotentialWorkCellsGlobal(pawn); }
                    catch (Exception e)
                    {
                        Log.Warning($"[RimMind-Actions] GetWorkTargets: PotentialWorkCellsGlobal threw for {wg.defName}: {e.Message}");
                        continue;
                    }
                    if (cells == null) continue;

                    foreach (var cell in cells)
                    {
                        if (!seenCells.Add(cell)) continue;
                        bool hasJob = false;
                        try { hasJob = scanner.HasJobOnCell(pawn, cell, false); }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimMind-Actions] GetWorkTargets: HasJobOnCell threw for {wg.defName} at {cell}: {ex.Message}");
                        }
                        if (!hasJob) continue;

                        string zoneLabel = pawn.Map.zoneManager?.ZoneAt(cell)?.label ?? wg.gerund ?? workType.labelShort;
                        string plantLabel = pawn.Map.thingGrid.ThingAt(cell, ThingCategory.Plant)?.LabelShort ?? "";
                        string label = plantLabel.NullOrEmpty()
                            ? $"{zoneLabel} ({cell.x},{cell.z})"
                            : $"{plantLabel} [{zoneLabel}]";

                        result.Add(new WorkTargetInfo
                        {
                            Label = label,
                            DefName = "",
                            Position = cell,
                            Distance = pawn.Position.DistanceTo(cell),
                        });
                        if (result.Count >= cap) break;
                    }
                }
                // ── Thing-based WorkGiver ──────────────────────────────────────
                else
                {
                    IEnumerable<Thing>? things = null;
                    try { things = scanner.PotentialWorkThingsGlobal(pawn); }
                    catch (Exception e)
                    {
                        Log.Warning($"[RimMind-Actions] GetWorkTargets: PotentialWorkThingsGlobal threw for {wg.defName}: {e.Message}");
                        continue;
                    }
                    if (things == null) continue;

                    foreach (var thing in things)
                    {
                        if (!seenThings.Add(thing)) continue;
                        bool hasJob = false;
                        try { hasJob = scanner.HasJobOnThing(pawn, thing, false); }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimMind-Actions] GetWorkTargets: HasJobOnThing threw for {wg.defName}: {ex.Message}");
                        }
                        if (!hasJob) continue;

                        // 对 DoBill 工作台额外显示账单名称；对拆除/卸载 WorkGiver 附加动作标签
                        string label = GetThingLabel(thing, scanner);

                        result.Add(new WorkTargetInfo
                        {
                            Label = label,
                            DefName = thing.def.defName,
                            Position = thing.Position,
                            Distance = pawn.Position.DistanceTo(thing.Position),
                        });
                        if (result.Count >= cap) break;
                    }
                }

                if (result.Count >= cap) break;
            }

            // 如果 Growing 扫描未找到目标，使用兜底逻辑直接读 Zone_Growing
            if (result.Count == 0 && workTypeDefName == "Growing")
                FallbackScanGrowing(pawn, result, cap);

            // 按距离排序，截取前 maxCount 条
            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (result.Count > maxCount)
                result.RemoveRange(maxCount, result.Count - maxCount);

            return result;
        }

        /// <summary>
        /// Growing 兜底扫描：当标准 WorkGiver 扫描未找到目标时，
        /// 直接读 Zone_Growing，查找可收割植物或空置可播种格子。
        /// </summary>
        private static void FallbackScanGrowing(Pawn pawn, List<WorkTargetInfo> result, int cap)
        {
            var map = pawn.Map;
            if (map?.zoneManager == null) return;

            foreach (var zone in map.zoneManager.AllZones)
            {
                if (zone is not Zone_Growing growZone) continue;
                if (growZone.cells.Count == 0) continue;

                // ── 查找可收割植物 ────────────────────────────────────────────
                foreach (var cell in growZone.cells)
                {
                    if (result.Count >= cap) break;
                    var plant = map.thingGrid.ThingAt(cell, ThingCategory.Plant) as Plant;
                    if (plant == null) continue;
                    if (plant.def.plant?.Harvestable != true) continue;
                    if (plant.LifeStage != PlantLifeStage.Mature) continue;

                    result.Add(new WorkTargetInfo
                    {
                        Label = "RimMind.Actions.Prompt.Harvestable".Translate(plant.LabelShort, growZone.label),
                        DefName = "",
                        Position = cell,
                        Distance = pawn.Position.DistanceTo(cell),
                    });
                }

                // ── 查找可播种的空格子 ────────────────────────────────────────
                if (result.Count < cap && growZone.allowSow)
                {
                    var wantedDef = growZone.GetPlantDefToGrow();
                    if (wantedDef != null)
                    {
                        int emptyCells = 0;
                        var firstEmptyCell = IntVec3.Invalid;
                        foreach (var cell in growZone.cells)
                        {
                            if (map.thingGrid.ThingAt(cell, ThingCategory.Plant) == null)
                            {
                                emptyCells++;
                                if (!firstEmptyCell.IsValid) firstEmptyCell = cell;
                            }
                        }
                        if (emptyCells > 0 && firstEmptyCell.IsValid)
                        {
                            result.Add(new WorkTargetInfo
                            {
                                Label = "RimMind.Actions.Prompt.WaitSow".Translate(wantedDef.LabelCap, $"{emptyCells}", growZone.label),
                                DefName = "",
                                Position = firstEmptyCell,
                                Distance = pawn.Position.DistanceTo(firstEmptyCell),
                            });
                        }
                    }
                }

                if (result.Count >= cap) break;
            }
        }

        /// <summary>
        /// 为 Thing 生成可读标签。
        /// - IBillGiver（烹饪台/手工台等）：附加第一条活跃账单名称。
        /// - 拆除/卸载 WorkGiver：附加 "(拆除)" / "(卸载)" 后缀以区别于建造任务。
        /// </summary>
        private static string GetThingLabel(Thing thing, WorkGiver_Scanner? scanner = null)
        {
            string baseLabel = thing.LabelShort;

            // IBillGiver：烹饪台、手工台、冶炼炉等
            if (thing is IBillGiver billGiver)
            {
                var bill = billGiver.BillStack?.FirstShouldDoNow;
                if (bill != null)
                    return $"{baseLabel} → {bill.LabelCap}";
            }

            // 拆除 / 卸载（同属 Construction WorkType，附加动作标签以区分）
            if (scanner is WorkGiver_Deconstruct)
                return "RimMind.Actions.Prompt.Deconstruct".Translate(baseLabel);
            if (scanner is WorkGiver_Uninstall)
                return "RimMind.Actions.Prompt.Uninstall".Translate(baseLabel);

            return baseLabel;
        }
    }

    // ─────────────────────────────────────────────
    //  move_to
    // ─────────────────────────────────────────────
    public class MoveToAction : IActionRule
    {
        public string IntentId => "move_to";
        public string DisplayName => "RimMind.Actions.DisplayName.MoveTo".Translate();
        public RiskLevel RiskLevel => RiskLevel.Low;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Map coordinates as x,z (e.g. '120,200')\"}},\"required\":[\"param\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.Map == null) return false;
            if (string.IsNullOrEmpty(param)) return false;

            var parts = param!.Split(',');
            if (parts.Length < 2 ||
                !int.TryParse(parts[0].Trim(), out int x) ||
                !int.TryParse(parts[1].Trim(), out int z))
            {
                Log.Warning($"[RimMind-Actions] move_to: bad param '{param}' (expected 'x,z')");
                return false;
            }

            var cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(actor.Map))
            {
                Log.Warning($"[RimMind-Actions] move_to: cell {cell} out of bounds");
                return false;
            }

            actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, cell), JobTag.Misc, requestQueueing);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  draft
    // ─────────────────────────────────────────────
    public class DraftAction : IActionRule
    {
        public string IntentId => "draft";
        public string DisplayName => "RimMind.Actions.DisplayName.Draft".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public string? ParameterSchema => null;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.Dead) return false;
            if (actor.drafter == null) return false;
            actor.drafter.Drafted = true;
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  undraft
    // ─────────────────────────────────────────────
    public class UndraftAction : IActionRule
    {
        public string IntentId => "undraft";
        public string DisplayName => "RimMind.Actions.DisplayName.Undraft".Translate();
        public RiskLevel RiskLevel => RiskLevel.Low;
        public string? ParameterSchema => null;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.Dead) return false;
            if (actor.drafter == null) return false;
            actor.drafter.Drafted = false;
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  tend_pawn
    // ─────────────────────────────────────────────
    public class TendPawnAction : IActionRule
    {
        public string IntentId => "tend_pawn";
        public string DisplayName => "RimMind.Actions.DisplayName.TendPawn".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;
            actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.TendPatient, target), JobTag.Misc, requestQueueing);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  rescue_pawn
    // ─────────────────────────────────────────────
    public class RescuePawnAction : IActionRule
    {
        public string IntentId => "rescue_pawn";
        public string DisplayName => "RimMind.Actions.DisplayName.RescuePawn".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Downed pawn short name\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null || !target.Downed) return false;
            actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Rescue, target), JobTag.Misc, requestQueueing);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  arrest_pawn
    // ─────────────────────────────────────────────
    public class ArrestPawnAction : IActionRule
    {
        public string IntentId => "arrest_pawn";
        public string DisplayName => "RimMind.Actions.DisplayName.ArrestPawn".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name to arrest\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;
            actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Arrest, target), JobTag.Misc, requestQueueing);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  cancel_job
    // ─────────────────────────────────────────────
    public class CancelJobAction : IActionRule
    {
        public string IntentId => "cancel_job";
        public string DisplayName => "RimMind.Actions.DisplayName.CancelJob".Translate();
        public RiskLevel RiskLevel => RiskLevel.Low;
        public string? ParameterSchema => null;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.jobs?.curJob == null) return false;
            actor.jobs.EndCurrentJob(JobCondition.Incompletable, false);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  set_work_priority
    // ─────────────────────────────────────────────
    public class SetWorkPriorityAction : IActionRule
    {
        public string IntentId => "set_work_priority";
        public string DisplayName => "RimMind.Actions.DisplayName.SetWorkPriority".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Format: WorkType,priority (0-4). E.g. Mining,1\"}},\"required\":[\"param\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;
            var parts = param!.Split(',');
            if (parts.Length < 2 || !int.TryParse(parts[1].Trim(), out int priority))
            {
                Log.Warning($"[RimMind-Actions] set_work_priority: bad param '{param}' (expected 'WorkType,priority')");
                return false;
            }

            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(parts[0].Trim());
            if (workType == null || actor.workSettings == null) return false;

            priority = Math.Min(Math.Max(priority, 0), 4);
            actor.workSettings.SetPriority(workType, priority);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  drop_weapon（High 风险）
    // ─────────────────────────────────────────────
    public class DropWeaponAction : IActionRule
    {
        public string IntentId => "drop_weapon";
        public string DisplayName => "RimMind.Actions.DisplayName.DropWeapon".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public string? ParameterSchema => null;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.equipment == null) return false;
            var weapon = actor.equipment.Primary;
            if (weapon == null) return false;

            actor.equipment.TryDropEquipment(weapon, out _, actor.Position, true);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  eat_food
    //  param：食物关键词（大小写不敏感，匹配 defName 或 Label，如 "Chocolate"），留空则自动寻找最近可食用食物。
    // ─────────────────────────────────────────────
    public class EatFoodAction : IActionRule
    {
        public string IntentId => "eat_food";
        public string DisplayName => "RimMind.Actions.DisplayName.EatFood".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Optional food keyword (defName or label). Empty = auto find nearest edible food.\"}},\"required\":[]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.Map == null) return false;

            Thing? food = FindFood(actor, param);
            if (food == null)
            {
                Log.Warning($"[RimMind-Actions] eat_food: no food found (param='{param}') for {actor.Name.ToStringShort}");
                return false;
            }

            if (!actor.CanReserveAndReach(food, PathEndMode.ClosestTouch, Danger.Some))
                return false;

            var job = JobMaker.MakeJob(JobDefOf.Ingest, food);
            int stackCount = FoodUtility.WillIngestStackCountOf(actor, food.def,
                FoodUtility.NutritionForEater(actor, food));
            job.count = stackCount < 1 ? 1 : stackCount;
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing);
            return true;
        }

        private static Thing? FindFood(Pawn pawn, string? keyword)
        {
            // 先搜背包
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (var t in pawn.inventory.innerContainer)
                {
                    if (t.def.IsIngestible && Matches(t, keyword) && pawn.WillEat(t))
                        return t;
                }
            }

            // 再搜地图（按距离优先）
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                validator: t =>
                    t.def.IsIngestible &&
                    Matches(t, keyword) &&
                    pawn.WillEat(t) &&
                    pawn.CanReserve(t, maxPawns: 10, stackCount: 1)
            );
        }

        private static bool Matches(Thing t, string? keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return true;
            string kw = keyword!.ToLowerInvariant();
            return t.def.defName.ToLowerInvariant().Contains(kw)
                || t.LabelShort.ToLowerInvariant().Contains(kw);
        }

        // ── 静态工具：枚举地图上可食用的愉悦食物（供 Advisor 构建候选）─────────
        /// <summary>
        /// 返回地图上小人可食用的、joy &gt; 0 的食物名称列表（去重，最多 <paramref name="max"/> 种）。
        /// </summary>
        public static List<string> GetJoyFoodLabels(Pawn pawn, int max = 5)
        {
            if (pawn.Map == null) return new List<string>();

            var seen = new HashSet<string>();
            var result = new List<string>();

            // 背包
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (var t in pawn.inventory.innerContainer)
                {
                    if (t.def.ingestible?.joy > 0f && pawn.WillEat(t) && seen.Add(t.def.defName))
                    {
                        result.Add(t.LabelShort);
                        if (result.Count >= max) return result;
                    }
                }
            }

            // 地图
            foreach (var t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (t.def.ingestible?.joy > 0f &&
                    pawn.WillEat(t) &&
                    pawn.CanReserve(t, maxPawns: 10, stackCount: 1) &&
                    seen.Add(t.def.defName))
                {
                    result.Add(t.LabelShort);
                    if (result.Count >= max) return result;
                }
            }
            return result;
        }
    }
}
