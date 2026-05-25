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

    public class ActionResult
    {
        public string ActionName { get; set; } = "";
        public bool Success { get; set; }
        public string Reason { get; set; } = "";
        public override string ToString() => Success ? $"OK: {ActionName}" : $"FAIL: {ActionName} ({Reason})";
    }

    public static class RimMindActionsAPI
    {
        public static void RegisterAction(string intentId, object rule) { }

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

        public static int ExecuteBatch(IReadOnlyList<BatchActionIntent> intents) => 0;

        public static List<ActionResult> ExecuteBatchWithResults(IReadOnlyList<BatchActionIntent> intents)
            => new List<ActionResult>();

        public static IReadOnlyList<string> GetSupportedIntents() => Array.Empty<string>();

        public static IReadOnlyList<(string intentId, string displayName, string riskLevel)> GetActionDescriptions()
            => Array.Empty<(string, string, string)>();

        public static string GetActionListText(Pawn? pawn = null) => "Actions module is deprecated. Use RimMindAPI.Tools.";

        public static bool IsAllowed(string intentId) => false;

        public static object? GetRiskLevel(string intentId) => null;

        public static List<WorkTargetInfo> GetWorkTargets(Pawn pawn, string workTypeDefName, int maxCount)
            => new List<WorkTargetInfo>();

        public static string? GetActionHintData(Pawn pawn, string intentId) => null;
    }

    public class WorkTargetInfo
    {
        public float Distance { get; set; }
        public string Label { get; set; } = "";
    }
}
