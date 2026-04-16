using System;
using System.Collections.Generic;
using Verse;

namespace RimMind.Actions.Queue
{
    /// <summary>
    /// GameComponent：延迟动作执行队列。
    /// Advisor 通过此组件投递动作，避免在 AI 回调（非主线程）中直接执行游戏逻辑。
    /// </summary>
    public class DelayedActionQueue : GameComponent
    {
        private static DelayedActionQueue? _instance;
        public static DelayedActionQueue Instance => _instance!;

        // 待执行动作列表（仅主线程访问）
        private readonly List<PendingAction> _queue = new List<PendingAction>();

        // RimWorld 用 Activator.CreateInstance(type, game) 创建 GameComponent，必须保留此签名
        public DelayedActionQueue(Game game)
        {
            _instance = this;
        }

        /// <summary>
        /// 入队一个延迟动作。
        /// </summary>
        /// <param name="intentId">动作意图 ID</param>
        /// <param name="actor">执行小人</param>
        /// <param name="target">目标小人（可选）</param>
        /// <param name="param">附加参数（可选）</param>
        /// <param name="reason">AI 给出的理由（用于气泡显示）</param>
        /// <param name="delaySeconds">延迟执行秒数（默认 1.5s，±20% 随机波动）</param>
        public void Enqueue(
            string intentId,
            Pawn actor,
            Pawn? target = null,
            string? param = null,
            string? reason = null,
            float delaySeconds = 1.5f)
        {
            float jitter = delaySeconds * 0.2f * (Rand.Value * 2f - 1f); // ±20%
            _queue.Add(new PendingAction
            {
                Id            = Guid.NewGuid().ToString("N"),
                IntentId      = intentId,
                Actor         = actor,
                Target        = target,
                Param         = param,
                Reason        = reason,
                TimeRemaining = delaySeconds + jitter,
                RiskLevel     = RimMindActionsAPI.GetRiskLevel(intentId) ?? RiskLevel.Low
            });
        }

        /// <summary>
        /// 返回队列中所有动作的调试信息字符串列表（仅供 Dev 菜单使用）。
        /// </summary>
        public List<string> GetPendingDebugInfo()
        {
            var result = new List<string>(_queue.Count);
            foreach (var p in _queue)
            {
                result.Add($"  [{p.RiskLevel}] {p.IntentId} | actor:{p.Actor?.Name?.ToStringShort ?? "?"}" +
                           $"{(p.Target != null ? $" -> {p.Target.Name.ToStringShort}" : "")}" +
                           $"{(p.Param != null ? $" param={p.Param}" : "")}" +
                           $" remaining:{p.TimeRemaining:F1}s" +
                           $"{(p.IsCancelled ? " [cancelled]" : "")}");
            }
            return result;
        }

        /// <summary>
        /// 取消指定小人的所有待执行动作。
        /// </summary>
        public void CancelForPawn(Pawn actor)
        {
            foreach (var pending in _queue)
            {
                if (pending.Actor == actor)
                    pending.IsCancelled = true;
            }
        }

        public override void GameComponentTick()
        {
            if (_queue.Count == 0) return;

            float dt = 1f / 60f; // 每 Tick ≈ 1/60 秒（RimWorld 60 ticks/s）

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var pending = _queue[i];

                if (pending.IsCancelled || pending.Actor == null ||
                    pending.Actor.Dead || pending.Actor.Destroyed)
                {
                    _queue.RemoveAt(i);
                    continue;
                }

                pending.TimeRemaining -= dt;

                if (pending.TimeRemaining > 0f) continue;

                // 到期执行
                try
                {
                    RimMindActionsAPI.Execute(pending.IntentId, pending.Actor, pending.Target, pending.Param);
                }
                catch (Exception e)
                {
                    Log.Error($"[RimMind-Actions] DelayedActionQueue: execute '{pending.IntentId}' failed: {e}");
                }
                _queue.RemoveAt(i);
            }
        }

        public override void ExposeData()
        {
            // PendingAction 中含 Pawn 引用（不可跨存档序列化），队列不做存档
            // 加载存档时队列自然为空，Advisor 下次 tick 重新评估
        }
    }

    /// <summary>
    /// 待执行动作记录。
    /// </summary>
    public class PendingAction
    {
        public string Id            = "";
        public string IntentId      = "";
        public Pawn Actor           = null!;
        public Pawn? Target;
        public string? Param;
        public string? Reason;
        public float TimeRemaining;
        public bool IsCancelled;
        public RiskLevel RiskLevel;
    }
}
