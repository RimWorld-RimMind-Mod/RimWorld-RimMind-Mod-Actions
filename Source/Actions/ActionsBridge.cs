using System.Collections.Generic;
using RimMind.Contracts.Client;
using RimMind.Contracts.Extensions;
using Verse;

namespace RimMind.Actions
{
    public class ActionsBridge : IAgentActionBridge
    {
        public void ExecuteAction(string npcId, string actionName, string[]? args = null)
        {
            var pawn = FindById(npcId);
            if (pawn != null)
                RimMindActionsAPI.Execute(actionName, pawn, null, args != null ? string.Join(" ", args) : null);
        }

        public bool CanExecute(string npcId, string actionName)
        {
            return RimMindActionsAPI.IsAllowed(actionName);
        }

        public bool CanExecute(object pawn, string action)
        {
            return RimMindActionsAPI.IsAllowed(action);
        }

        public void Execute(object pawn, string action, string? targetName = null)
        {
            if (pawn is Pawn p)
                RimMindActionsAPI.Execute(action, p, null, targetName);
        }

        public List<StructuredTool>? GetAvailableTools(object pawn)
        {
            return RimMindActionsAPI.GetStructuredTools();
        }

        private static Pawn? FindById(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            foreach (var map in Find.Maps)
                foreach (var p in map.mapPawns.AllPawns)
                    if ($"NPC-{p.thingIDNumber}" == npcId) return p;
            return null;
        }
    }
}
