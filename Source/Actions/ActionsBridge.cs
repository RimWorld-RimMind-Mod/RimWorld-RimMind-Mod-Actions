using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Client;

namespace RimMind.Actions
{
    [Obsolete("ActionsBridge is deprecated. Use RimMindAPI.Tools instead.")]
    public class ActionsBridge : IAgentActionBridge
    {
        [Obsolete]
        public void ExecuteAction(string npcId, string actionName, string[]? args = null) { }

        [Obsolete]
        public bool CanExecute(string npcId, string actionName) => false;

        [Obsolete]
        public bool CanExecute(object pawn, string action) => false;

        [Obsolete]
        public void Execute(object pawn, string action, string? targetName = null) { }

        [Obsolete]
        public List<StructuredTool>? GetAvailableTools(object pawn) => null;
    }
}
