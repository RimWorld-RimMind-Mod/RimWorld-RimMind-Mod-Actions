using System;
using System.Collections.Generic;
using RimMind.Actions.Actions;
using Verse;

namespace RimMind.Actions
{
    /// <summary>
    /// 批量动作意图描述。用于 ExecuteBatch。
    /// </summary>
    public class BatchActionIntent
    {
        public string   IntentId  = "";
        public Pawn     Actor     = null!;
        public Pawn?    Target;
        public string?  Param;
        public string?  Reason;    // 用于调用方显示气泡，Actions 本身不处理
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
            bool requestQueueing = false)
        {
            if (!_rules.TryGetValue(intentId, out var rule))
            {
                Log.Warning($"[RimMind-Actions] Unknown intentId: {intentId}");
                return false;
            }

            // 检查玩家设置：该动作是否被允许
            if (RimMindActionsMod.Settings != null &&
                !RimMindActionsMod.Settings.IsAllowed(intentId))
            {
                Log.Message($"[RimMind-Actions] '{intentId}' is disabled by player settings, skipping.");
                return false;
            }

            return rule.Execute(actor, target, param, requestQueueing);
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
            if (intents == null || intents.Count == 0) return 0;

            // 记录每个 Pawn 已处理的第一个 Job 类动作（之后的追加模式）
            var firstJobSent = new HashSet<Pawn>(ReferenceEqualityComparer.Instance);
            int successCount = 0;

            foreach (var intent in intents)
            {
                if (!_rules.TryGetValue(intent.IntentId, out var rule))
                {
                    Log.Warning($"[RimMind-Actions] ExecuteBatch: Unknown intentId: {intent.IntentId}");
                    continue;
                }

                // 非 Job 类动作（即时效果）直接以 false 执行，不影响队列逻辑
                bool isJobAction = rule.IsJobBased;
                bool requestQueueing = isJobAction && firstJobSent.Contains(intent.Actor);

                bool ok = rule.Execute(intent.Actor, intent.Target, intent.Param, requestQueueing);
                if (ok)
                {
                    successCount++;
                    if (isJobAction)
                        firstJobSent.Add(intent.Actor);
                }
            }

            return successCount;
        }

        // ── 查询 ──────────────────────────────────────────────

        /// <summary>
        /// 返回所有已注册意图 ID（供 Advisor 构建候选列表）。
        /// </summary>
        public static IReadOnlyList<string> GetSupportedIntents()
        {
            return new List<string>(_rules.Keys);
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
            if (RimMindActionsMod.Settings == null) return true; // 设置未初始化时放行
            return RimMindActionsMod.Settings.IsAllowed(intentId);
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
