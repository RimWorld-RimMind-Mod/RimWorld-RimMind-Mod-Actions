using System;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace RimMind.Actions.Actions
{
    // ─────────────────────────────────────────────
    //  recruit_agree（High 风险）
    // ─────────────────────────────────────────────
    public class RecruitAgreeAction : IActionRule
    {
        public string IntentId => "recruit_agree";
        public string DisplayName => "RimMind.Actions.DisplayName.RecruitAgree".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name to recruit\"}},\"required\":[\"target\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            // actor 是要被招募的 NPC；需要有招募者（recruiter）上下文
            // 简化：直接以 target（招募者）触发，actor 为被招募目标
            var recruit = actor;
            var recruiter = target;

            if (recruit == null || recruit.Faction == Faction.OfPlayer) return false;

            try
            {
                var lord = recruit.GetLord();
                if (lord != null)
                {
                    lord.RemovePawn(recruit);
                }

                recruit.SetFaction(Faction.OfPlayer, recruiter);

                recruit.guest?.SetGuestStatus(null);

                Find.LetterStack.ReceiveLetter(
                    "LetterLabelMessageRecruitSuccess".Translate() + ": " + recruit.LabelShort,
                    "MessageRecruitSuccess".Translate(recruiter?.LabelShort ?? "AI", recruit.LabelShort, Faction.OfPlayer.Name),
                    LetterDefOf.PositiveEvent,
                    recruit);

                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimMind-Actions] recruit_agree failed: {e}");
                return false;
            }
        }
    }

    // ─────────────────────────────────────────────
    //  adjust_faction（High 风险）
    // ─────────────────────────────────────────────
    public class AdjustFactionAction : IActionRule
    {
        public string IntentId => "adjust_faction";
        public string DisplayName => "RimMind.Actions.DisplayName.AdjustFaction".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"Format: FactionDef,delta (e.g. Outlander,-20) or just delta (e.g. +10,-5) when target pawn's faction is used. Delta range: -100 to 100\"},\"target\":{\"type\":\"string\",\"description\":\"Target pawn short name - will adjust goodwill with this pawn's faction\"}},\"required\":[\"param\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;

            int delta;
            Faction? faction = null;

            var parts = param!.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out delta))
            {
                var factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(parts[0].Trim());
                faction = Find.FactionManager.FirstFactionOfDef(factionDef);
            }
            else if (int.TryParse(parts[0].Trim(), out delta))
            {
                if (target != null)
                    faction = target.Faction;
            }
            else
            {
                Log.Warning($"[RimMind-Actions] adjust_faction: bad param '{param}' (expected 'FactionDef,delta' or just 'delta')");
                return false;
            }

            if (faction == null || faction == Faction.OfPlayer) return false;

            delta = Math.Min(Math.Max(delta, -100), 100);
            Faction.OfPlayer.TryAffectGoodwillWith(faction, delta, true, true);
            return true;
        }
    }
}
