using RimWorld;
using Verse;

namespace RimMind.Actions.Actions
{
    public class InspireWorkAction : IActionRule
    {
        public string IntentId => "inspire_work";
        public string DisplayName => "RimMind.Actions.DisplayName.InspireWork".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name\"}},\"required\":[]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            var pawn = target ?? actor;
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail("Frenzy_Work");
            if (def == null || pawn.mindState?.inspirationHandler == null) return false;
            return pawn.mindState.inspirationHandler.TryStartInspiration(def);
        }
    }

    public class InspireShootAction : IActionRule
    {
        public string IntentId => "inspire_shoot";
        public string DisplayName => "RimMind.Actions.DisplayName.InspireShoot".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name\"}},\"required\":[]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            var pawn = target ?? actor;
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail("Frenzy_Shoot");
            if (def == null || pawn.mindState?.inspirationHandler == null) return false;
            return pawn.mindState.inspirationHandler.TryStartInspiration(def);
        }
    }

    public class InspireTradeAction : IActionRule
    {
        public string IntentId => "inspire_trade";
        public string DisplayName => "RimMind.Actions.DisplayName.InspireTrade".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name\"}},\"required\":[]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            var pawn = target ?? actor;
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail("Inspired_Trade");
            if (def == null || pawn.mindState?.inspirationHandler == null) return false;
            return pawn.mindState.inspirationHandler.TryStartInspiration(def);
        }
    }

    public class AddThoughtAction : IActionRule
    {
        public string IntentId => "add_thought";
        public string DisplayName => "RimMind.Actions.DisplayName.AddThought".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"ThoughtDef defName to add\"}},\"required\":[\"param\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.Dead) return false;
            if (string.IsNullOrEmpty(param)) return false;

            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(param!);
            if (def == null)
            {
                Log.Warning($"[RimMind-Actions] add_thought: unknown ThoughtDef '{param}'");
                return false;
            }
            if (actor.needs?.mood?.thoughts?.memories == null) return false;

            actor.needs.mood.thoughts.memories.TryGainMemory(def);
            return true;
        }
    }

    public class TriggerMentalStateAction : IActionRule
    {
        public string IntentId => "trigger_mental_state";
        public string DisplayName => "RimMind.Actions.DisplayName.TriggerMentalState".Translate();
        public RiskLevel RiskLevel => RiskLevel.Critical;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"MentalStateDef defName (e.g. MentalState_Manhunter, MentalState_WanderPsychotic)\"}},\"required\":[\"param\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;

            if (actor.Faction != Faction.OfPlayer) return false;
            if (actor.InMentalState) return false;

            var def = DefDatabase<MentalStateDef>.GetNamedSilentFail(param!);
            if (def == null)
            {
                Log.Warning($"[RimMind-Actions] trigger_mental_state: unknown MentalStateDef '{param}'");
                return false;
            }

            return actor.mindState.mentalStateHandler.TryStartMentalState(def);
        }
    }
}
