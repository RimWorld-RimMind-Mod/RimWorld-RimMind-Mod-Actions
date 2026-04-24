using System;
using System.Collections.Generic;
using RimMind.Actions.Actions;
using RimMind.Core;
using RimMind.Core.AgentBus;
using RimMind.Core.Client;
using Verse;

namespace RimMind.Actions
{
    /// <summary>
    /// 批量动作意图描述。用于 ExecuteBatch。
    /// </summary>
    public class BatchActionIntent
    {
        public string IntentId = "";
        public Pawn Actor = null!;
        public Pawn? Target;
        public string? Param;
        public string? Reason;
        public string? EventId;
    }

    /// <summary>
    /// 公共静态 API。Advisor、Dialogue 等 mod 通过此类执行动作。
    /// </summary>
    public static class RimMindActionsAPI
    {
        private static readonly Dictionary<string, IActionRule> _rules =
            new Dictionary<string, IActionRule>();

        // ── 注册 ──────────────────────────────────────────────

        /// <summary>
        /// 注册动作规则。内置动作在 RimMindActionsMod 构造时注册；外部 mod 可按需追加。
        /// </summary>
        public static void RegisterAction(string intentId, IActionRule rule)
        {
            _rules[intentId] = rule;
            _ruleVersion++;
        }

        // ── 单条执行 ──────────────────────────────────────────

        /// <summary>
        /// 执行指定意图。找不到意图或执行失败均返回 false。
        /// </summary>
        /// <param name="requestQueueing">
        ///   true = 追加到 Job 队列末尾（不清队列，不打断当前 Job）；
        ///   false = 打断当前 Job，清队列后立即执行（默认）。
        ///   对非 Job 类动作无效。
        /// </param>
        public static bool Execute(
            string intentId,
            Pawn actor,
            Pawn? target = null,
            string? param = null,
            bool requestQueueing = false,
            string? eventId = null)
        {
            return ExecuteWithResult(intentId, actor, target, param, requestQueueing, eventId).Success;
        }

        public static ActionResult ExecuteWithResult(
            string intentId,
            Pawn actor,
            Pawn? target = null,
            string? param = null,
            bool requestQueueing = false,
            string? eventId = null)
        {
            string targetLabel = target?.LabelShort ?? "";

            if (actor == null)
            {
                var nullResult = ActionResult.Failed(intentId, "Actor is null", targetLabel);
                PublishActionEvent(actor, nullResult, eventId);
                return nullResult;
            }

            if (RimMindActionsMod.Settings != null && !RimMindActionsMod.Settings.enableActions)
            {
                var disabledResult = ActionResult.Failed(intentId, "Actions disabled", targetLabel);
                return disabledResult;
            }

            if (!_rules.TryGetValue(intentId, out var rule))
            {
                Log.Warning($"[RimMind-Actions] Unknown intentId: {intentId}");
                var failResult = ActionResult.Failed(intentId, "Unknown intent", targetLabel);
                PublishActionEvent(actor, failResult, eventId);
                return failResult;
            }

            if (RimMindAPI.ShouldSkipAction(intentId))
            {
                Log.Message($"[RimMind-Actions] '{intentId}' skipped by bridge skip check.");
                var skipResult = ActionResult.Failed(intentId, "Skipped by bridge", targetLabel);
                PublishActionEvent(actor, skipResult, eventId);
                return skipResult;
            }

            if (RimMindActionsMod.Settings != null &&
                !RimMindActionsMod.Settings.IsAllowed(intentId))
            {
                Log.Message($"[RimMind-Actions] '{intentId}' is disabled by player settings, skipping.");
                var disabledResult = ActionResult.Failed(intentId, "Disabled by player", targetLabel);
                PublishActionEvent(actor, disabledResult, eventId);
                return disabledResult;
            }

            bool ok = rule.Execute(actor, target, param, requestQueueing);
            var result = ok
                ? ActionResult.Succeeded(intentId, targetLabel)
                : ActionResult.Failed(intentId, "Execution failed", targetLabel);
            PublishActionEvent(actor, result, eventId);
            return result;
        }

        // ── 批量执行 ──────────────────────────────────────────

        /// <summary>
        /// 批量执行多个动作意图。
        ///
        /// <para><b>同一小人的 Job 序列</b>：第一个动作 requestQueueing=false（打断当前任务，清队列，重新开始），
        /// 后续动作 requestQueueing=true（EnqueueLast，追加到队列尾部）。
        /// 饥饿/睡眠打断只丢失当前正在执行的步骤，队列中其余步骤保留。</para>
        ///
        /// <para><b>不同小人</b>：互不影响，全部 requestQueueing=false 独立执行。</para>
        ///
        /// <para><b>非 Job 类动作</b>（add_thought、inspire 等）：requestQueueing 参数无效，直接执行。</para>
        /// </summary>
        /// <returns>成功执行的条数</returns>
        public static int ExecuteBatch(IReadOnlyList<BatchActionIntent> intents)
        {
            var results = ExecuteBatchWithResults(intents);
            int success = 0;
            for (int i = 0; i < results.Count; i++)
                if (results[i].Success) success++;
            return success;
        }

        public static List<ActionResult> ExecuteBatchWithResults(IReadOnlyList<BatchActionIntent> intents)
        {
            var results = new List<ActionResult>();
            if (intents == null || intents.Count == 0) return results;

            var firstJobSent = new HashSet<Pawn>(ReferenceEqualityComparer.Instance);

            foreach (var intent in intents)
            {
                string targetLabel = intent.Target?.LabelShort ?? "";

                if (intent.Actor == null)
                {
                    results.Add(ActionResult.Failed(intent.IntentId, "Actor is null", targetLabel));
                    continue;
                }

                if (RimMindActionsMod.Settings != null && !RimMindActionsMod.Settings.enableActions)
                {
                    results.Add(ActionResult.Failed(intent.IntentId, "Actions disabled", targetLabel));
                    continue;
                }

                if (!_rules.TryGetValue(intent.IntentId, out var rule))
                {
                    Log.Warning($"[RimMind-Actions] ExecuteBatch: Unknown intentId: {intent.IntentId}");
                    results.Add(ActionResult.Failed(intent.IntentId, "Unknown intent", targetLabel));
                    continue;
                }

                if (RimMindAPI.ShouldSkipAction(intent.IntentId))
                {
                    results.Add(ActionResult.Failed(intent.IntentId, "Skipped by bridge", targetLabel));
                    PublishActionEvent(intent.Actor, results[results.Count - 1], intent.EventId);
                    continue;
                }

                if (RimMindActionsMod.Settings != null &&
                    !RimMindActionsMod.Settings.IsAllowed(intent.IntentId))
                {
                    results.Add(ActionResult.Failed(intent.IntentId, "Disabled by player", targetLabel));
                    PublishActionEvent(intent.Actor, results[results.Count - 1], intent.EventId);
                    continue;
                }

                bool isJobAction = rule.IsJobBased;
                bool requestQueueing = isJobAction && firstJobSent.Contains(intent.Actor);

                bool ok = rule.Execute(intent.Actor, intent.Target, intent.Param, requestQueueing);
                var result = ok
                    ? ActionResult.Succeeded(intent.IntentId, targetLabel)
                    : ActionResult.Failed(intent.IntentId, "Execution failed", targetLabel);
                results.Add(result);

                if (ok && isJobAction)
                    firstJobSent.Add(intent.Actor);

                PublishActionEvent(intent.Actor, result, intent.EventId);
            }

            return results;
        }

        // ── 查询 ──────────────────────────────────────────────

        /// <summary>
        /// 返回所有已注册意图 ID（供 Advisor 构建候选列表）。
        /// </summary>
        private static List<string>? _cachedIntentList;
        private static int _cachedIntentVersion;
        private static int _ruleVersion;

        public static IReadOnlyList<string> GetSupportedIntents()
        {
            if (_cachedIntentList == null || _cachedIntentVersion != _ruleVersion)
            {
                _cachedIntentList = new List<string>(_rules.Keys);
                _cachedIntentVersion = _ruleVersion;
            }
            return _cachedIntentList;
        }

        /// <summary>
        /// 返回所有已注册动作的 (intentId, displayName, riskLevel) 三元组，供 Advisor 构建候选列表。
        /// </summary>
        public static IReadOnlyList<(string intentId, string displayName, RiskLevel riskLevel)> GetActionDescriptions()
        {
            var list = new List<(string, string, RiskLevel)>(_rules.Count);
            foreach (var kv in _rules)
                list.Add((kv.Key, kv.Value.DisplayName, kv.Value.RiskLevel));
            return list;
        }

        public static List<StructuredTool> GetStructuredTools()
        {
            var tools = new List<StructuredTool>(_rules.Count);
            foreach (var kv in _rules)
            {
                var rule = kv.Value;
                if (RimMindActionsMod.Settings != null && !RimMindActionsMod.Settings.IsAllowed(rule.IntentId))
                    continue;
                tools.Add(new StructuredTool
                {
                    Name = rule.IntentId,
                    Description = rule.DisplayName,
                    Parameters = rule.ParameterSchema,
                    ToolChoice = null,
                });
            }
            return tools;
        }

        /// <summary>
        /// 获取指定意图的风险级别（找不到返回 null）。
        /// </summary>
        public static RiskLevel? GetRiskLevel(string intentId)
        {
            return _rules.TryGetValue(intentId, out var rule) ? rule.RiskLevel : (RiskLevel?)null;
        }

        /// <summary>
        /// 枚举指定小人对某工作类型可执行的具体目标列表，按距离排序。
        ///
        /// <para>供 RimMind-Advisor 的 JobCandidateBuilder 调用：将结果写入 prompt，
        /// 让 AI 可以选择"采矿@45,32"这样的具体目标，而不仅是"采矿"这个类型。</para>
        ///
        /// <para>param 格式参见 <see cref="WorkTargetInfo.ToParam"/>：
        /// <c>"Mining@45,32"</c> 直接作为 assign_work 的 param 传入。</para>
        /// </summary>
        /// <param name="pawn">执行小人</param>
        /// <param name="workTypeDefName">WorkTypeDef 的 defName，如 "Mining"</param>
        /// <param name="maxCount">最多返回多少个目标（默认 8，避免 prompt 过长）</param>
        public static List<WorkTargetInfo> GetWorkTargets(
            Pawn pawn, string workTypeDefName, int maxCount = 8)
            => AssignWorkAction.GetWorkTargets(pawn, workTypeDefName, maxCount);

        /// <summary>
        /// 检查指定意图是否被玩家设置允许执行。
        /// Advisor 可在构建候选列表时调用，过滤掉被禁用的动作。
        /// </summary>
        public static bool IsAllowed(string intentId)
        {
            if (RimMindActionsMod.Settings == null) return true;
            return RimMindActionsMod.Settings.IsAllowed(intentId);
        }

        private static void PublishActionEvent(Pawn actor, ActionResult result, string? eventId = null)
        {
            if (actor == null) return;
            string npcId = $"NPC-{actor.thingIDNumber}";
            global::RimMind.Core.AgentBus.AgentBus.Publish(new ActionEvent(npcId, actor.thingIDNumber, result.ActionName, result.Success, result.Reason, result.TargetLabel, eventId ?? ""));
        }

    }

    /// <summary>
    /// 引用相等比较器，用于 HashSet&lt;Pawn&gt;（按对象引用去重，不依赖 Pawn.Equals 重写）。
    /// </summary>
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<Pawn>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public bool Equals(Pawn x, Pawn y) => ReferenceEquals(x, y);
        public int GetHashCode(Pawn obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
