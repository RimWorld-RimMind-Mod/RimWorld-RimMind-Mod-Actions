using LudeonTK;
using RimMind.Actions.Queue;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Debug
{
    /// <summary>
    /// 开发模式调试动作（Dev 菜单 → RimMind Actions）。
    /// 对小人的操作均需先在地图上选中小人，再点菜单。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ActionsDebugActions
    {
        // ══════════════════════════════════════════════════════════
        // 状态检查
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "Show Registered Intents",
            actionType = DebugActionType.Action)]
        public static void ShowRegisteredIntents()
        {
            var sb = new StringBuilder("=== RimMind-Actions Registered Intents ===\n");
            var intents = RimMindActionsAPI.GetSupportedIntents();
            sb.AppendLine($"Total {intents.Count} intents:\n");
            foreach (var id in intents)
            {
                var risk = RimMindActionsAPI.GetRiskLevel(id);
                sb.AppendLine($"  [{risk ?? RiskLevel.Low}] {id}");
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind Actions", "Show Job State (selected)",
            actionType = DebugActionType.Action)]
        public static void ShowJobState()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind-Actions] Please select a pawn on the map first.");
                return;
            }

            var sb = new StringBuilder($"=== {pawn.Name.ToStringShort} Job State ===\n");
            var curJob = pawn.jobs.curJob;
            sb.AppendLine($"Current Job: {curJob?.def?.defName ?? "(none)"}");
            if (curJob != null)
            {
                sb.AppendLine($"  playerForced: {curJob.playerForced}");
                sb.AppendLine($"  targetA: {curJob.targetA.ToStringSafe()}");
            }
            // 判断"AI 可接管"：无任务，或属于主动漫游/等待类（不算有意义的工作）
            bool isIdle = IsAdvisorIdle(pawn);
            sb.AppendLine($"AI can take over (idle): {(isIdle ? "yes" : "no")}");
            sb.AppendLine($"  -> Rule: Job=null or belongs to Wait/Wait_Wander/GotoWander/Wait_MaintainPosture");
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind Actions", "Show DelayedActionQueue",
            actionType = DebugActionType.Action)]
        public static void ShowDelayedQueue()
        {
            var queue = DelayedActionQueue.Instance;
            if (queue == null)
            {
                Log.Warning("[RimMind-Actions] DelayedActionQueue not initialized (requires a loaded save).");
                return;
            }

            var sb = new StringBuilder("=== DelayedActionQueue Pending Actions ===\n");
            var actions = queue.GetPendingDebugInfo();
            if (actions.Count == 0)
                sb.AppendLine("(queue empty)");
            else
                foreach (var info in actions)
                    sb.AppendLine(info);
            Log.Message(sb.ToString());
        }

        // ══════════════════════════════════════════════════════════
        // PawnActions
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "PawnAction: force_rest (selected)",
            actionType = DebugActionType.Action)]
        public static void TestForceRest()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("force_rest", p);
            Log.Message($"[RimMind-Actions] force_rest -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: assign_work Mining (selected)",
            actionType = DebugActionType.Action)]
        public static void TestAssignWorkMining()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("assign_work", p, param: "Mining");
            Log.Message($"[RimMind-Actions] assign_work Mining -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed (no available mining target or work not enabled)")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: assign_work Construction (selected)",
            actionType = DebugActionType.Action)]
        public static void TestAssignWorkConstruction()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("assign_work", p, param: "Construction");
            Log.Message($"[RimMind-Actions] assign_work Construction -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: assign_work Cooking (selected)",
            actionType = DebugActionType.Action)]
        public static void TestAssignWorkCooking()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("assign_work", p, param: "Cooking");
            Log.Message($"[RimMind-Actions] assign_work Cooking -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: move_to center (selected)",
            actionType = DebugActionType.Action)]
        public static void TestMoveTo()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var map = p.Map;
            if (map == null) return;
            int cx = map.Size.x / 2;
            int cz = map.Size.z / 2;
            bool ok = RimMindActionsAPI.Execute("move_to", p, param: $"{cx},{cz}");
            Log.Message($"[RimMind-Actions] move_to {cx},{cz} -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: draft (selected)",
            actionType = DebugActionType.Action)]
        public static void TestDraft()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("draft", p);
            Log.Message($"[RimMind-Actions] draft -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: undraft (selected)",
            actionType = DebugActionType.Action)]
        public static void TestUndraft()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("undraft", p);
            Log.Message($"[RimMind-Actions] undraft -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: cancel_job (selected)",
            actionType = DebugActionType.Action)]
        public static void TestCancelJob()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            string curJobName = p.jobs.curJob?.def?.defName ?? "(none)";
            bool ok = RimMindActionsAPI.Execute("cancel_job", p);
            Log.Message($"[RimMind-Actions] cancel_job (was:{curJobName}) -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: set_work_priority Mining=1 (selected)",
            actionType = DebugActionType.Action)]
        public static void TestSetWorkPriority()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("set_work_priority", p, param: "Mining,1");
            Log.Message($"[RimMind-Actions] set_work_priority Mining=1 -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: drop_weapon [HIGH RISK] (selected)",
            actionType = DebugActionType.Action)]
        public static void TestDropWeapon()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            string weaponName = p.equipment?.Primary?.Label ?? "(no weapon)";
            bool ok = RimMindActionsAPI.Execute("drop_weapon", p);
            Log.Message($"[RimMind-Actions] drop_weapon ({weaponName}) -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed (no weapon)")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: eat_food (selected)",
            actionType = DebugActionType.Action)]
        public static void TestEatFood()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("eat_food", p);
            Log.Message($"[RimMind-Actions] eat_food -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed (no reachable food)")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: give_item Medicine (selected)",
            actionType = DebugActionType.Action)]
        public static void TestGiveItemMedicine()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var target = p.Map?.mapPawns.FreeColonists.FirstOrDefault(x => x != p);
            if (target == null)
            {
                Log.Warning("[RimMind-Actions] give_item requires at least two colonists on the map.");
                return;
            }
            bool ok = RimMindActionsAPI.Execute("give_item", p, target: target, param: "Medicine");
            Log.Message($"[RimMind-Actions] give_item Medicine -> {p.Name.ToStringShort} -> {target.Name.ToStringShort}: {(ok ? "ok" : "failed (no Medicine in inventory)")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: tend_pawn (selected)",
            actionType = DebugActionType.Action)]
        public static void TestTendPawn()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var injuredPawn = p.Map?.mapPawns.FreeColonists.FirstOrDefault(x => x != p && x.health.HasHediffsNeedingTend());
            if (injuredPawn == null)
            {
                Log.Warning("[RimMind-Actions] tend_pawn: no injured colonist found on the map.");
                return;
            }
            bool ok = RimMindActionsAPI.Execute("tend_pawn", p, target: injuredPawn);
            Log.Message($"[RimMind-Actions] tend_pawn -> {p.Name.ToStringShort} -> {injuredPawn.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "PawnAction: rescue_pawn (selected)",
            actionType = DebugActionType.Action)]
        public static void TestRescuePawn()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var downedPawn = p.Map?.mapPawns.FreeColonists.FirstOrDefault(x => x != p && x.Downed);
            if (downedPawn == null)
            {
                Log.Warning("[RimMind-Actions] rescue_pawn: no downed colonist found on the map.");
                return;
            }
            bool ok = RimMindActionsAPI.Execute("rescue_pawn", p, target: downedPawn);
            Log.Message($"[RimMind-Actions] rescue_pawn -> {p.Name.ToStringShort} -> {downedPawn.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        // ══════════════════════════════════════════════════════════
        // SocialActions
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "SocialAction: social_relax (selected)",
            actionType = DebugActionType.Action)]
        public static void TestSocialRelax()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("social_relax", p);
            Log.Message($"[RimMind-Actions] social_relax -> {p.Name.ToStringShort}: {(ok ? "ok (Chitchat + Joy timetable)" : "failed")}");
        }

        // ══════════════════════════════════════════════════════════
        // MoodActions
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "MoodAction: inspire_work (selected)",
            actionType = DebugActionType.Action)]
        public static void TestInspireWork()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("inspire_work", p);
            Log.Message($"[RimMind-Actions] inspire_work -> {p.Name.ToStringShort}: {(ok ? "ok (Frenzy_Work)" : "failed (already inspired or condition not met)")}");
        }

        [DebugAction("RimMind Actions", "MoodAction: inspire_shoot (selected)",
            actionType = DebugActionType.Action)]
        public static void TestInspireShoot()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("inspire_shoot", p);
            Log.Message($"[RimMind-Actions] inspire_shoot -> {p.Name.ToStringShort}: {(ok ? "ok (Frenzy_Shoot)" : "failed")}");
        }

        [DebugAction("RimMind Actions", "MoodAction: inspire_trade (selected)",
            actionType = DebugActionType.Action)]
        public static void TestInspireTrade()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("inspire_trade", p);
            Log.Message($"[RimMind-Actions] inspire_trade -> {p.Name.ToStringShort}: {(ok ? "ok (Inspired_Trade)" : "failed")}");
        }

        [DebugAction("RimMind Actions", "MoodAction: add_thought Catharsis (selected)",
            actionType = DebugActionType.Action)]
        public static void TestAddThoughtCatharsis()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            bool ok = RimMindActionsAPI.Execute("add_thought", p, param: "Catharsis");
            Log.Message($"[RimMind-Actions] add_thought Catharsis -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        [DebugAction("RimMind Actions", "MoodAction: add_thought SleptOutside (selected)",
            actionType = DebugActionType.Action)]
        public static void TestAddThoughtSleptOutside()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            // SleptOutside 是 vanilla 内置 ThoughtDef（户外睡眠负面心情）
            bool ok = RimMindActionsAPI.Execute("add_thought", p, param: "SleptOutside");
            Log.Message($"[RimMind-Actions] add_thought SleptOutside -> {p.Name.ToStringShort}: {(ok ? "ok (-8 mood)" : "failed (ThoughtDef missing)")}");
        }

        [DebugAction("RimMind Actions", "[CRITICAL] trigger_mental_state Wander_Sad (selected)",
            actionType = DebugActionType.Action)]
        public static void TestTriggerMentalState()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            Log.Warning($"[RimMind-Actions] [CRITICAL] Triggering mental break (Wander_Sad) for {p.Name.ToStringShort}...");
            bool ok = RimMindActionsAPI.Execute("trigger_mental_state", p, param: "Wander_Sad");
            Log.Message($"[RimMind-Actions] trigger_mental_state -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed (already in mental state / in combat)")}");
        }

        // ══════════════════════════════════════════════════════════
        // RelationActions
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "RelationAction: adjust_faction Outlander +10",
            actionType = DebugActionType.Action)]
        public static void TestAdjustFactionPositive()
        {
            if (!TryGetFactionAndColonist(out var faction, out var colonist)) return;
            float before = faction!.GoodwillWith(Faction.OfPlayer);
            bool ok = RimMindActionsAPI.Execute("adjust_faction", colonist!, param: $"{faction.def.defName},10");
            float after = faction.GoodwillWith(Faction.OfPlayer);
            Log.Message($"[RimMind-Actions] adjust_faction {faction.Name} +10: {(ok ? $"ok ({before:F0} -> {after:F0})" : "failed")}");
        }

        [DebugAction("RimMind Actions", "RelationAction: adjust_faction Outlander -10",
            actionType = DebugActionType.Action)]
        public static void TestAdjustFactionNegative()
        {
            if (!TryGetFactionAndColonist(out var faction, out var colonist)) return;
            float before = faction!.GoodwillWith(Faction.OfPlayer);
            bool ok = RimMindActionsAPI.Execute("adjust_faction", colonist!, param: $"{faction.def.defName},-10");
            float after = faction.GoodwillWith(Faction.OfPlayer);
            Log.Message($"[RimMind-Actions] adjust_faction {faction.Name} -10: {(ok ? $"ok ({before:F0} -> {after:F0})" : "failed")}");
        }

        // ══════════════════════════════════════════════════════════
        // EventActions
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "[CRITICAL] trigger_incident ResourcePodCrash",
            actionType = DebugActionType.Action)]
        public static void TestTriggerIncidentResourcePod()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Warning("[RimMind-Actions] No active map."); return; }
            var colonist = map.mapPawns.FreeColonists.FirstOrDefault();
            if (colonist == null) { Log.Warning("[RimMind-Actions] No colonist found."); return; }
            Log.Warning("[RimMind-Actions] [CRITICAL] Triggering ResourcePodCrash...");
            bool ok = RimMindActionsAPI.Execute("trigger_incident", colonist, param: "ResourcePodCrash");
            Log.Message($"[RimMind-Actions] trigger_incident ResourcePodCrash: {(ok ? "ok" : "failed (condition not met or Def missing)")}");
        }

        // ══════════════════════════════════════════════════════════
        // assign_work 目标查询与精确指定
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "WorkTargets: list Mining targets (selected)",
            actionType = DebugActionType.Action)]
        public static void TestListMiningTargets()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;

            var targets = RimMindActionsAPI.GetWorkTargets(p, "Mining", maxCount: 8);
            if (targets.Count == 0)
            {
                Log.Message($"[RimMind-Actions] {p.Name.ToStringShort} no mineable ore found (mining not enabled / no designated / no reachable ore).");
                return;
            }

            var sb = new StringBuilder($"=== {p.Name.ToStringShort} Mining Targets (by distance) ===\n");
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                sb.AppendLine($"  {i + 1}. {t}  param=\"{t.ToParam("Mining")}\"");
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind Actions", "WorkTargets: list Construction targets (selected)",
            actionType = DebugActionType.Action)]
        public static void TestListConstructionTargets()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            ListTargets(p, "Construction");
        }

        [DebugAction("RimMind Actions", "WorkTargets: list Cooking targets (selected)",
            actionType = DebugActionType.Action)]
        public static void TestListCookingTargets()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            // Cooking 目标 = 工作台 + 账单名称，如 "电炉 → 简单餐×5"
            ListTargets(p, "Cooking");
        }

        [DebugAction("RimMind Actions", "WorkTargets: list Crafting targets (selected)",
            actionType = DebugActionType.Action)]
        public static void TestListCraftingTargets()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            ListTargets(p, "Crafting");
        }

        [DebugAction("RimMind Actions", "WorkTargets: list Growing targets (selected)",
            actionType = DebugActionType.Action)]
        public static void TestListGrowingTargets()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            // Growing 是 cell-based，显示种植区+植物名
            ListTargets(p, "Growing");
        }

        [DebugAction("RimMind Actions", "WorkTargets: list Hauling targets (selected)",
            actionType = DebugActionType.Action)]
        public static void TestListHaulingTargets()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            ListTargets(p, "Hauling");
        }

        [DebugAction("RimMind Actions", "assign_work: nearest Mining target (selected)",
            actionType = DebugActionType.Action)]
        public static void TestAssignWorkNearestMining()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;

            var targets = RimMindActionsAPI.GetWorkTargets(p, "Mining", maxCount: 1);
            if (targets.Count == 0)
            {
                Log.Message($"[RimMind-Actions] {p.Name.ToStringShort} no available mining targets.");
                return;
            }

            string paramStr = targets[0].ToParam("Mining"); // "Mining@45,32"
            bool ok = RimMindActionsAPI.Execute("assign_work", p, param: paramStr);
            Log.Message($"[RimMind-Actions] assign_work exact target param={paramStr} -> {p.Name.ToStringShort}: {(ok ? "ok" : "failed")}");
        }

        // ══════════════════════════════════════════════════════════
        // 批量执行测试
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "Batch: move_to + social_relax same pawn (selected)",
            actionType = DebugActionType.Action)]
        public static void TestBatchSamePawn()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var map = p.Map;
            if (map == null) return;
            int cx = map.Size.x / 2;
            int cz = map.Size.z / 2;

            var intents = new List<BatchActionIntent>
            {
                new BatchActionIntent { IntentId = "move_to",      Actor = p, Param = $"{cx},{cz}", Reason = "[Debug] Move to map center" },
                new BatchActionIntent { IntentId = "social_relax", Actor = p,                       Reason = "[Debug] Relax after arrival" },
            };
            int count = RimMindActionsAPI.ExecuteBatch(intents);
            Log.Message($"[RimMind-Actions] Batch(same pawn move_to+social_relax) -> {p.Name.ToStringShort}: {count}/{intents.Count} ok");
        }

        [DebugAction("RimMind Actions", "Batch: multi-pawn force_rest + inspire_work",
            actionType = DebugActionType.Action)]
        public static void TestBatchMultiPawn()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Warning("[RimMind-Actions] No active map."); return; }
            var colonists = map.mapPawns.FreeColonists.Take(3).ToList();
            if (colonists.Count < 2)
            {
                Log.Warning("[RimMind-Actions] Batch multi-pawn test requires at least 2 colonists.");
                return;
            }

            var intents = new List<BatchActionIntent>();
            for (int i = 0; i < colonists.Count; i++)
            {
                string action = (i % 2 == 0) ? "force_rest" : "inspire_work";
                intents.Add(new BatchActionIntent { IntentId = action, Actor = colonists[i], Reason = $"[Debug] {action}" });
            }
            int count = RimMindActionsAPI.ExecuteBatch(intents);
            string names = string.Join(", ", colonists.Select(c => c.Name.ToStringShort));
            Log.Message($"[RimMind-Actions] Batch(multi-pawn) [{names}]: {count}/{intents.Count} ok");
        }

        [DebugAction("RimMind Actions", "Batch: 5-step job sequence (selected)",
            actionType = DebugActionType.Action)]
        public static void TestBatchJobSequence()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var map = p.Map;
            if (map == null) return;
            int cx = map.Size.x / 2;
            int cz = map.Size.z / 2;

            var intents = new List<BatchActionIntent>
            {
                new BatchActionIntent { IntentId = "move_to",      Actor = p, Param = $"{cx},{cz}", Reason = "[Debug] Step 1: Move to center" },
                new BatchActionIntent { IntentId = "assign_work",  Actor = p, Param = "Mining",     Reason = "[Debug] Step 2: Mining" },
                new BatchActionIntent { IntentId = "assign_work",  Actor = p, Param = "Construction", Reason = "[Debug] Step 3: Construction" },
                new BatchActionIntent { IntentId = "assign_work",  Actor = p, Param = "Cooking",    Reason = "[Debug] Step 4: Cooking" },
                new BatchActionIntent { IntentId = "force_rest",   Actor = p,                       Reason = "[Debug] Step 5: Rest" },
            };
            int count = RimMindActionsAPI.ExecuteBatch(intents);
            Log.Message($"[RimMind-Actions] 5-step sequence -> {p.Name.ToStringShort}: {count}/{intents.Count} ok enqueued\n" +
                        "(step 1 requestQueueing=false, rest requestQueueing=true)");
        }

        // ══════════════════════════════════════════════════════════
        // DelayedActionQueue 测试
        // ══════════════════════════════════════════════════════════

        [DebugAction("RimMind Actions", "DelayedQueue: enqueue force_rest in 3s (selected)",
            actionType = DebugActionType.Action)]
        public static void TestDelayedForceRest()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var queue = DelayedActionQueue.Instance;
            if (queue == null) { Log.Warning("[RimMind-Actions] DelayedActionQueue not initialized."); return; }
            queue.Enqueue("force_rest", p, null, null, "[Debug] Delayed force_rest in 3s", delaySeconds: 3f);
            Log.Message($"[RimMind-Actions] force_rest enqueued with delay (3s) -> {p.Name.ToStringShort}");
        }

        [DebugAction("RimMind Actions", "DelayedQueue: cancel all for selected pawn",
            actionType = DebugActionType.Action)]
        public static void TestCancelDelayedForPawn()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!ValidatePawn(pawn, out var p)) return;
            var queue = DelayedActionQueue.Instance;
            if (queue == null) { Log.Warning("[RimMind-Actions] DelayedActionQueue not initialized."); return; }
            queue.CancelForPawn(p);
            Log.Message($"[RimMind-Actions] Cancelled all delayed actions for {p.Name.ToStringShort}.");
        }

        // ══════════════════════════════════════════════════════════
        // 辅助方法
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 判断小人当前是否处于 AI 顾问可接管的空闲状态：
        /// 无任务，或仅在执行漫游/等待类 Job（非玩家指派）。
        /// </summary>
        private static bool IsAdvisorIdle(Pawn pawn)
        {
            var job = pawn.jobs.curJob;
            if (job == null) return true;
            if (job.playerForced) return false;
            return job.def == JobDefOf.Wait
                || job.def == JobDefOf.Wait_Wander
                || job.def == JobDefOf.GotoWander
                || job.def == JobDefOf.Wait_MaintainPosture;
        }

        /// <summary>
        /// 验证选中小人，成功时通过 out 参数返回非空引用。
        /// </summary>
        private static bool ValidatePawn(Pawn? pawn, out Pawn result)
        {
            result = null!;
            if (pawn == null)
            {
                Log.Warning("[RimMind-Actions] Please select a pawn on the map first before opening Dev menu.");
                return false;
            }
            if (!pawn.IsColonist)
            {
                Log.Warning($"[RimMind-Actions] {pawn.Name.ToStringShort} is not a colonist, skipping.");
                return false;
            }
            result = pawn;
            return true;
        }

        private static void ListTargets(Pawn p, string workTypeName)
        {
            var targets = RimMindActionsAPI.GetWorkTargets(p, workTypeName, maxCount: 8);
            if (targets.Count == 0)
            {
                Log.Message($"[RimMind-Actions] {p.Name.ToStringShort} no available {workTypeName} targets" +
                            " (work type not enabled / no active bills / no reachable targets).");
                return;
            }
            var sb = new StringBuilder($"=== {p.Name.ToStringShort} {workTypeName} Targets (by distance) ===\n");
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                sb.AppendLine($"  {i + 1}. {t}  param=\"{t.ToParam(workTypeName)}\"");
            }
            Log.Message(sb.ToString());
        }

        private static bool TryGetFactionAndColonist(out Faction? faction, out Pawn? colonist)
        {
            faction = null;
            colonist = null;

            var map = Find.CurrentMap;
            if (map == null) { Log.Warning("[RimMind-Actions] No active map."); return false; }

            colonist = map.mapPawns.FreeColonists.FirstOrDefault();
            if (colonist == null) { Log.Warning("[RimMind-Actions] No colonist found."); return false; }

            faction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => !f.IsPlayer && !f.defeated && f.def.defName.Contains("Outlander"));
            faction ??= Find.FactionManager.AllFactions.FirstOrDefault(f => !f.IsPlayer && !f.defeated);

            if (faction == null) { Log.Warning("[RimMind-Actions] No usable faction found."); return false; }
            return true;
        }
    }
}
