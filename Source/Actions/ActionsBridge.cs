using System.Collections.Generic;
using RimMind.Core.Agent;
using RimMind.Core.Client;
using Verse;

namespace RimMind.Actions
{
    public class ActionsBridge : IAgentActionBridge
    {
        public bool Execute(string intentId, Pawn actor, Pawn? target, string? param, string? eventId = null)
        {
            return RimMindActionsAPI.Execute(intentId, actor, target, param, eventId: eventId);
        }

        public List<StructuredTool> GetAvailableTools(Pawn pawn)
        {
            return RimMindActionsAPI.GetStructuredTools();
        }

        Core.Agent.RiskLevel? IAgentActionBridge.GetRiskLevel(string intentId)
        {
            var actionsRisk = RimMindActionsAPI.GetRiskLevel(intentId);
            if (actionsRisk == null) return null;
            return actionsRisk switch
            {
                RiskLevel.Low => Core.Agent.RiskLevel.Low,
                RiskLevel.Medium => Core.Agent.RiskLevel.Medium,
                RiskLevel.High => Core.Agent.RiskLevel.High,
                RiskLevel.Critical => Core.Agent.RiskLevel.Critical,
                _ => Core.Agent.RiskLevel.High
            };
        }
    }
}
