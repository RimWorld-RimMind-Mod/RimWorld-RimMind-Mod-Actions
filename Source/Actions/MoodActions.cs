using RimWorld;
using Verse;

namespace RimMind.Actions.Actions
{
    // ─────────────────────────────────────────────
    //  inspire_work
    // ─────────────────────────────────────────────
    public class InspireWorkAction : IActionRule
    {
        public string IntentId    => "inspire_work";
        public string DisplayName => "RimMind.Actions.DisplayName.InspireWork".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail("Frenzy_Work");
            if (def == null || actor.mindState?.inspirationHandler == null) return false;
            return actor.mindState.inspirationHandler.TryStartInspiration(def);
        }
    }

    // ─────────────────────────────────────────────
    //  inspire_fight
    // ─────────────────────────────────────────────
    public class InspireFightAction : IActionRule
    {
        public string IntentId    => "inspire_fight";
        public string DisplayName => "RimMind.Actions.DisplayName.InspireFight".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail("Frenzy_Shoot");
            if (def == null || actor.mindState?.inspirationHandler == null) return false;
            return actor.mindState.inspirationHandler.TryStartInspiration(def);
        }
    }

    // ─────────────────────────────────────────────
    //  inspire_trade
    // ─────────────────────────────────────────────
    public class InspireTradeAction : IActionRule
    {
        public string IntentId    => "inspire_trade";
        public string DisplayName => "RimMind.Actions.DisplayName.InspireTrade".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            var def = DefDatabase<InspirationDef>.GetNamedSilentFail("Inspired_Trade");
            if (def == null || actor.mindState?.inspirationHandler == null) return false;
            return actor.mindState.inspirationHandler.TryStartInspiration(def);
        }
    }

    // ─────────────────────────────────────────────
    //  add_thought
    // ─────────────────────────────────────────────
    public class AddThoughtAction : IActionRule
    {
        public string IntentId    => "add_thought";
        public string DisplayName => "RimMind.Actions.DisplayName.AddThought".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
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

    // ─────────────────────────────────────────────
    //  trigger_mental_state（Critical 风险）
    // ─────────────────────────────────────────────
    public class TriggerMentalStateAction : IActionRule
    {
        public string IntentId    => "trigger_mental_state";
        public string DisplayName => "RimMind.Actions.DisplayName.TriggerMentalState".Translate();
        public RiskLevel RiskLevel => RiskLevel.Critical;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;

            // 安全限制：仅对玩家殖民者、非战斗中触发
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
