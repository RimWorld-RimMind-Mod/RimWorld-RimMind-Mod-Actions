using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace RimMind.Actions.Queue
{
    public class DelayedActionQueue : GameComponent
    {
        private static DelayedActionQueue? _instance;
        public static DelayedActionQueue? Instance => _instance;

        private List<PendingAction> _queue = new List<PendingAction>();
        private readonly ConcurrentQueue<PendingAction> _incoming = new ConcurrentQueue<PendingAction>();

        public DelayedActionQueue(Game game)
        {
            _instance = this;
        }

        public void Enqueue(
            string intentId,
            Pawn actor,
            Pawn? target = null,
            string? param = null,
            string? reason = null,
            float delaySeconds = -1f)
        {
            float effectiveDelay = delaySeconds >= 0f ? delaySeconds : (RimMindActionsMod.Settings?.delayedQueueDefaultDelay ?? 1.5f);
            int effectiveDelayTicks = (int)(effectiveDelay * 60f);
            int maxSize = RimMindActionsMod.Settings?.delayedQueueMaxSize ?? 50;
            if (_incoming.Count >= maxSize)
            {
                Log.Warning($"[RimMind-Actions] DelayedActionQueue: queue full ({maxSize}), dropping '{intentId}'");
                return;
            }

            _incoming.Enqueue(new PendingAction
            {
                IntentId = intentId,
                Actor = actor,
                Target = target,
                Param = param,
                Reason = reason,
                TicksRemaining = effectiveDelayTicks,
                RiskLevel = RimMindActionsAPI.GetRiskLevel(intentId) ?? RiskLevel.Low
            });
        }

        public List<string> GetPendingDebugInfo()
        {
            var result = new List<string>(_queue.Count);
            foreach (var p in _queue)
            {
                result.Add($"  [{p.RiskLevel}] {p.IntentId} | actor:{p.Actor?.Name?.ToStringShort ?? "?"}" +
                           $"{(p.Target != null ? $" -> {p.Target.Name.ToStringShort}" : "")}" +
                           $"{(p.Param != null ? $" param={p.Param}" : "")}" +
                           $" remaining:{p.TicksRemaining}t" +
                           $"{(p.IsCancelled ? " [cancelled]" : "")}");
            }
            return result;
        }

        public void CancelForPawn(Pawn actor)
        {
            foreach (var pending in _queue)
            {
                if (pending.Actor == actor)
                    pending.IsCancelled = true;
            }

            var temp = new List<PendingAction>();
            while (_incoming.TryDequeue(out var action))
            {
                if (action.Actor == actor)
                    action.IsCancelled = true;
                temp.Add(action);
            }
            foreach (var action in temp)
                _incoming.Enqueue(action);
        }

        public override void GameComponentTick()
        {
            DrainIncoming();
            ProcessQueue();
        }

        public void ProcessQueue()
        {
            if (_queue.Count == 0) return;

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var pending = _queue[i];

                if (pending.IsCancelled || pending.Actor == null ||
                    pending.Actor.Dead || pending.Actor.Destroyed)
                {
                    _queue.RemoveAt(i);
                    continue;
                }

                pending.TicksRemaining--;

                if (pending.TicksRemaining > 0) continue;

                try
                {
                    RimMindActionsAPI.Execute(pending.IntentId, pending.Actor, pending.Target, pending.Param);
                    if (!pending.Reason.NullOrEmpty())
                        Log.Message($"[RimMind-Actions] DelayedActionQueue executed '{pending.IntentId}' for {pending.Actor.Name?.ToStringShort}: {pending.Reason}");
                }
                catch (Exception e)
                {
                    Log.Error($"[RimMind-Actions] DelayedActionQueue: execute '{pending.IntentId}' failed: {e}");
                }
                _queue.RemoveAt(i);
            }
        }

        private void DrainIncoming()
        {
            int maxSize = RimMindActionsMod.Settings?.delayedQueueMaxSize ?? 50;
            while (_incoming.TryDequeue(out var action))
            {
                if (_queue.Count >= maxSize)
                {
                    Log.Warning($"[RimMind-Actions] DelayedActionQueue: queue full ({maxSize}), dropping '{action.IntentId}'");
                    continue;
                }
                int jitterTicks = (int)(action.TicksRemaining * 0.2f * (Rand.Value * 2f - 1f));
                action.TicksRemaining += jitterTicks;
                _queue.Add(action);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref _queue, "queue", LookMode.Deep);
        }
    }

    public class PendingAction : IExposable
    {
        public string IntentId = "";
        public Pawn Actor = null!;
        public Pawn? Target;
        public string? Param;
        public string? Reason;
        public int TicksRemaining;
        public bool IsCancelled;
        public RiskLevel RiskLevel;

        public void ExposeData()
        {
#pragma warning disable CS8601
            Scribe_Values.Look(ref IntentId, "intentId");
#pragma warning restore CS8601
            IntentId ??= "";
            Scribe_References.Look(ref Actor, "actor");
            Scribe_References.Look(ref Target, "target");
            Scribe_Values.Look(ref Param, "param");
            Scribe_Values.Look(ref Reason, "reason");
            Scribe_Values.Look(ref TicksRemaining, "ticksRemaining");
            Scribe_Values.Look(ref IsCancelled, "isCancelled");
            Scribe_Values.Look(ref RiskLevel, "riskLevel");
        }
    }
}
