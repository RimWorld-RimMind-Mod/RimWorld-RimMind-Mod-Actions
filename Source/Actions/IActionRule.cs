using Verse;

namespace RimMind.Actions
{
    /// <summary>
    /// 单条动作规则。每个 intentId 对应一个实现。
    /// </summary>
    public interface IActionRule
    {
        string IntentId    { get; }
        string DisplayName { get; }
        RiskLevel RiskLevel { get; }

        /// <summary>
        /// 是否为 Job 类动作（内部调用 TryTakeOrderedJob）。
        /// <para>
        /// <b>外部 mod 自定义动作须按需覆盖此属性：</b><br/>
        /// - 动作最终调用 <c>pawn.jobs.TryTakeOrderedJob</c> → 返回 <c>true</c><br/>
        /// - 动作直接修改状态（add_thought、inspire 等）→ 保持默认 <c>false</c>
        /// </para>
        /// <para>
        /// <see cref="RimMindActionsAPI.ExecuteBatch"/> 依赖此值决定同一小人多步序列中
        /// 是否使用 <c>requestQueueing=true</c>（EnqueueLast）以保留后续步骤。
        /// </para>
        /// </summary>
        bool IsJobBased => false;

        /// <summary>
        /// 执行动作。
        /// </summary>
        /// <param name="actor">执行动作的小人</param>
        /// <param name="target">目标小人（可选）</param>
        /// <param name="param">附加参数字符串（各动作自行解析）</param>
        /// <param name="requestQueueing">
        ///   true = 追加到小人 Job 队列末尾（EnqueueLast，不清队列）；
        ///   false = 打断当前 Job，清队列后立即执行（默认）。
        ///   对非 Job 类动作（add_thought、inspire 等）此参数无效。
        /// </param>
        /// <returns>执行成功返回 true，前置条件不满足返回 false</returns>
        bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false);
    }
}
