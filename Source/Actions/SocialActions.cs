using System.Collections.Generic;
using System.Linq;
using RimMind.Actions.Queue;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    public class SocialRelaxAction : IActionRule
    {
        private const string RestoreParamPrefix = "RIMIND_TT_RESTORE:";
        private const float RestoreDelaySeconds = 90f;

        private static readonly Dictionary<int, (TimeAssignmentDef def, int hour)> _pendingRestores =
            new Dictionary<int, (TimeAssignmentDef def, int hour)>();

        public string IntentId => "social_relax";
        public string DisplayName => "RimMind.Actions.DisplayName.SocialRelax".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => false;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name to socialize with\"}},\"required\":[]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (param != null && param.StartsWith(RestoreParamPrefix))
            {
                if (_pendingRestores.TryGetValue(actor.thingIDNumber, out var restore))
                {
                    actor.timetable?.SetAssignment(restore.hour, restore.def);
                    _pendingRestores.Remove(actor.thingIDNumber);
                }
                return true;
            }

            var intDef = DefDatabase<InteractionDef>.GetNamedSilentFail("Chitchat");
            if (intDef == null) return false;

            if (target != null && target != actor && actor.interactions.CanInteractNowWith(target, intDef))
            {
                actor.interactions.TryInteractWith(target, intDef);
            }
            else
            {
                var nearbyPawn = FindNearbySocializablePawn(actor, intDef);
                if (nearbyPawn != null)
                    actor.interactions.TryInteractWith(nearbyPawn, intDef);
            }

            if (actor.timetable != null)
            {
                int hour = GenLocalDate.HourOfDay(actor);
                TimeAssignmentDef originalDef = actor.timetable.GetAssignment(hour);
                if (originalDef != TimeAssignmentDefOf.Joy)
                {
                    _pendingRestores[actor.thingIDNumber] = (originalDef, hour);
                    actor.timetable.SetAssignment(hour, TimeAssignmentDefOf.Joy);
                    DelayedActionQueue.Instance?.Enqueue(
                        IntentId, actor, null,
                        RestoreParamPrefix,
                        "restore timetable after social_relax",
                        RestoreDelaySeconds);
                }
                else
                {
                    actor.timetable.SetAssignment(hour, TimeAssignmentDefOf.Joy);
                }
            }

            return true;
        }

        private static Pawn? FindNearbySocializablePawn(Pawn actor, InteractionDef intDef)
        {
            float bestDist = 30f;
            Pawn? best = null;
            foreach (var mapPawn in actor.Map?.mapPawns?.AllPawnsSpawned ?? System.Linq.Enumerable.Empty<Pawn>())
            {
                if (mapPawn == actor || !mapPawn.RaceProps.Humanlike || mapPawn.Dead || mapPawn.Downed) continue;
                if (!actor.interactions.CanInteractNowWith(mapPawn, intDef)) continue;
                float dist = actor.Position.DistanceTo(mapPawn.Position);
                if (dist < bestDist) { bestDist = dist; best = mapPawn; }
            }
            return best;
        }
    }

    public class GiveItemAction : IActionRule
    {
        public string IntentId => "give_item";
        public string DisplayName => "RimMind.Actions.DisplayName.GiveItem".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Item keyword (matches Label or defName, case-insensitive)\"},\"target\":{\"type\":\"string\",\"description\":\"Recipient pawn short name\"}},\"required\":[\"param\",\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;
            if (target == null) return false;
            if (actor.inventory?.innerContainer == null) return false;
            if (target.inventory?.innerContainer == null) return false;

            var keyword = param!.ToLowerInvariant();
            Thing? found = null;
            foreach (var thing in actor.inventory.innerContainer)
            {
                if (thing.Label.ToLowerInvariant().Contains(keyword) ||
                    thing.def.defName.ToLowerInvariant().Contains(keyword))
                {
                    found = thing;
                    break;
                }
            }
            if (found == null) return false;

            int stackToTake = System.Math.Min(1, found.stackCount);
            int transferred = actor.inventory.innerContainer.TryTransferToContainer(
                found, target.inventory.innerContainer, stackToTake, out _);
            return transferred > 0;
        }
    }

    public class RomanceAttemptAction : IActionRule
    {
        public string IntentId => "romance_attempt";
        public string DisplayName => "RimMind.Actions.DisplayName.RomanceAttempt".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name for romance\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;

            var intDef = DefDatabase<InteractionDef>.GetNamed("RomanceAttempt", false);
            if (intDef == null) return false;

            if (actor.interactions.CanInteractNowWith(target, intDef))
            {
                actor.interactions.TryInteractWith(target, intDef);
                return true;
            }

            actor.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(JobDefOf.Goto, target.Position),
                JobTag.Misc, requestQueueing);
            return false;
        }
    }

    public class RomanceBreakupAction : IActionRule
    {
        public string IntentId => "romance_breakup";
        public string DisplayName => "RimMind.Actions.DisplayName.RomanceBreakup".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public bool IsJobBased => true;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Partner pawn short name to break up with\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;

            var intDef = DefDatabase<InteractionDef>.GetNamed("Breakup", false);
            if (intDef == null) return false;

            if (actor.interactions.CanInteractNowWith(target, intDef))
            {
                actor.interactions.TryInteractWith(target, intDef);
                return true;
            }

            actor.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(JobDefOf.Goto, target.Position),
                JobTag.Misc, requestQueueing);
            return false;
        }
    }
}
