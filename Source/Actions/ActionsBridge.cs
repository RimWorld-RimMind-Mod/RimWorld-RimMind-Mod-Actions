using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Llm;

namespace RimMind.Actions
{
    /// <summary>
    /// ActionsBridge is an empty shell since H2 phase.
    /// All action implementations have been migrated to Core Mechanisms.
    /// Will serve as ICompositeToolCall registration entry point in the future.
    /// </summary>
    [Obsolete("ActionsBridge is an empty shell since H2. Use Core Mechanisms via ToolRegistry instead.")]
    public class ActionsBridge : IAgentActionBridge
    {
        public string Id => "ActionsBridge";
        public string OwnerModId => "RimMindActions";

        public void ExecuteAction(string npcId, string actionName, string[]? args = null) { }

        public bool CanExecute(string npcId, string actionName) => false;

        public bool CanExecute(object pawn, string action) => false;

        public void Execute(object pawn, string action, string? targetName = null) { }

        public List<StructuredTool>? GetAvailableTools(object pawn) => null;
    }
}
