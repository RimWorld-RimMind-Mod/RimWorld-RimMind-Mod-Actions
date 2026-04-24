using Verse;

namespace RimMind.Actions
{
    public interface IActionRule
    {
        string IntentId { get; }
        string DisplayName { get; }
        RiskLevel RiskLevel { get; }

        bool IsJobBased => false;

        string? ParameterSchema => null;

        bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false);
    }
}
