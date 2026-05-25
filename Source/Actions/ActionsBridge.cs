using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Llm;

namespace RimMind.Actions
{
    /// <summary>
    /// ActionsBridge will serve as ICompositeToolCall registration entry point
    /// for high-level intent execution orchestrating atomic ToolCalls.
    /// </summary>
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
