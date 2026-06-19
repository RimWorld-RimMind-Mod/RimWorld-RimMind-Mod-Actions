using System;
using System.Collections.Generic;
using Verse;

namespace RimMind.Actions
{
    [Obsolete("ActionResult is deprecated since H2. Use Result<ToolResult, RimMindError> instead. Removed when Actions module is fully decommissioned.")]
    public class BatchActionIntent
    {
        public string IntentId = "";
        public Pawn Actor = null!;
        public Pawn? Target;
        public string? Param;
        public string? Reason;
        public string? EventId;
    }

    [Obsolete("ActionResult is deprecated since H2. Use Result<ToolResult, RimMindError> instead. Removed when Actions module is fully decommissioned.")]
    public class ActionResult
    {
        public string ActionName { get; set; } = "";
        public bool Success { get; set; }
        public string Reason { get; set; } = "";
        public override string ToString() => Success ? $"OK: {ActionName}" : $"FAIL: {ActionName} ({Reason})";
    }

    /// <summary>
    /// [Deprecated since H2] All methods forward to RimMindAPI.Tools.
    /// Actions module is retained as ICompositeToolCall orchestration carrier.
    /// M-stage deletion has been cancelled per 2026-05-25 decision.
    /// </summary>
    public static class RimMindActionsAPI
    {
        [Obsolete("Use RimMindAPI.Tools.Register() instead.")]
        public static void RegisterAction(string intentId, object rule) { }

        [Obsolete("Use ToolCall dispatch via RimMindAPI.Send/SendAsync instead.")]
        public static bool Execute(
            string intentId,
            Pawn actor,
            Pawn? target = null,
            string? param = null,
            bool requestQueueing = false,
            string? eventId = null)
        {
            return false;
        }

        [Obsolete("Use ToolCall dispatch via RimMindAPI.Send/SendAsync instead.")]
        public static bool ExecuteWithResult(
            string intentId,
            Pawn actor,
            Pawn? target = null,
            string? param = null,
            bool requestQueueing = false,
            string? eventId = null)
        {
            return false;
        }

        [Obsolete("Use ToolCall dispatch via RimMindAPI.Send/SendAsync instead.")]
        public static int ExecuteBatch(IReadOnlyList<BatchActionIntent> intents) => 0;

        [Obsolete("Use ToolCall dispatch via RimMindAPI.Send/SendAsync instead.")]
        public static List<ActionResult> ExecuteBatchWithResults(IReadOnlyList<BatchActionIntent> intents)
            => new List<ActionResult>();

        [Obsolete("Use RimMindAPI.Tools.GetAllDefinitions() instead.")]
        public static IReadOnlyList<string> GetSupportedIntents() => Array.Empty<string>();

        [Obsolete("Use RimMindAPI.Tools.GetAllDefinitions() instead.")]
        public static IReadOnlyList<(string intentId, string displayName, string riskLevel)> GetActionDescriptions()
            => Array.Empty<(string, string, string)>();

        [Obsolete("Use RimMindAPI.Tools.GetAllDefinitions() instead.")]
        public static string GetActionListText(Pawn? pawn = null) => "Actions module is deprecated. Use RimMindAPI.Tools.";

        [Obsolete("Use RimMindAPI.Tools.FindById() instead.")]
        public static bool IsAllowed(string intentId) => false;

        [Obsolete("Use MechanismRisk from IGameMechanism instead.")]
        public static object? GetRiskLevel(string intentId) => null;

        [Obsolete("Use Mechanism ToolCall dispatch instead.")]
        public static List<WorkTargetInfo> GetWorkTargets(Pawn pawn, string workTypeDefName, int maxCount)
            => new List<WorkTargetInfo>();

        [Obsolete("Use Mechanism ToolCall dispatch instead.")]
        public static string? GetActionHintData(Pawn pawn, string intentId) => null;
    }

    [Obsolete("WorkTargetInfo is deprecated since H2.")]
    public class WorkTargetInfo
    {
        public float Distance { get; set; }
        public string Label { get; set; } = "";
    }
}
