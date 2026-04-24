using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    public class SocialDiningAction : IActionRule
    {
        public string IntentId => "social_dining";
        public string DisplayName => "RimMind.Actions.DisplayName.SocialDining".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name to share meal with\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;

            var intDef = DefDatabase<InteractionDef>.GetNamed("ShareMeal", false)
                      ?? DefDatabase<InteractionDef>.GetNamed("ChatFriendly", false);
            if (intDef == null) return false;

            if (!actor.interactions.CanInteractNowWith(target, intDef)) return false;
            actor.interactions.TryInteractWith(target, intDef);

            actor.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Catharsis);
            target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Catharsis);
            return true;
        }
    }

    public class SocialRelaxAction : IActionRule
    {
        public string IntentId => "social_relax";
        public string DisplayName => "RimMind.Actions.DisplayName.SocialRelax".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public string? ParameterSchema => null;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.needs?.mood?.thoughts?.memories == null) return false;

            actor.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.Catharsis);

            if (actor.timetable != null)
                actor.timetable.SetAssignment(GenLocalDate.HourOfDay(actor), TimeAssignmentDefOf.Joy);

            return true;
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

            int transferred = actor.inventory.innerContainer.TryTransferToContainer(
                found, target.inventory.innerContainer, found.stackCount, out _);
            return transferred > 0;
        }
    }

    public class RomanceAcceptAction : IActionRule
    {
        public string IntentId => "romance_accept";
        public string DisplayName => "RimMind.Actions.DisplayName.RomanceAccept".Translate();
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
            return true;
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
            return true;
        }
    }
}
