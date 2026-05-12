using System;
using System.Collections.Generic;
using Verse;

namespace RimMind.Actions
{
    public class BatchActionIntent
    {
        public string IntentId = "";
        public Pawn Actor = null!;
        public Pawn? Target;
        public string? Param;
        public string? Reason;
        public string? EventId;
    }

    [Obsolete("Use Result<bool, RimMindError> instead. This type is deprecated and will be removed in a future version.")]
    public class ActionResult
    {
        public string ActionName { get; set; } = "";
        public bool Success { get; set; }
        public string Reason { get; set; } = "";
        public override string ToString() => Success ? $"OK: {ActionName}" : $"FAIL: {ActionName} ({Reason})";
    }

    [Obsolete("Use RimMindAPI.Tools instead. This API is deprecated and will be removed in a future version.")]
    public static class RimMindActionsAPI
    {
        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static void RegisterAction(string intentId, object rule) { }

        [Obsolete("Use RimMindAPI.Tools instead.")]
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

        [Obsolete("Use RimMindAPI.Tools instead.")]
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

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static int ExecuteBatch(IReadOnlyList<BatchActionIntent> intents) => 0;

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static List<ActionResult> ExecuteBatchWithResults(IReadOnlyList<BatchActionIntent> intents)
            => new List<ActionResult>();

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static IReadOnlyList<string> GetSupportedIntents() => Array.Empty<string>();

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static IReadOnlyList<(string intentId, string displayName, string riskLevel)> GetActionDescriptions()
            => Array.Empty<(string, string, string)>();

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static string GetActionListText(Pawn? pawn = null) => "Actions module is deprecated. Use RimMindAPI.Tools.";

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static bool IsAllowed(string intentId) => false;

        [Obsolete("Use RimMindAPI.Tools instead.")]
        public static object? GetRiskLevel(string intentId) => null;
    }
}
