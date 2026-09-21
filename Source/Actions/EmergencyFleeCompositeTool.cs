using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool for emergency flee & battlefield evacuation.
    /// Cancels risky jobs, undrafts pawn to restore free movement, forces rest/shelter, and adds morale relief.
    /// </summary>
    public sealed class EmergencyFleeCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.emergency_flee";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Emergency flee and evacuation: interrupts current risky work, undrafts pawn if stuck, forces safe rest/shelter, and applies a calm survivor thought.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"},\"danger_level\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"pawn_id\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => new[]
        {
            "pawn.job.set",
            "pawn.draft.toggle",
            "pawn.thought.add"
        };

        public override async Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            var pawnIdResult = TryGetArgumentOrError<int>(args.ArgumentsJson, "pawn_id", args.TraceId);
            if (pawnIdResult.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(pawnIdResult.Error);
            }
            int pawnId = pawnIdResult.Value;

            // 1. Interrupt risky active job
            var cancelJobArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "cancel_job"));
            var cancelJob = await ExecuteAtomicAsync("pawn.job.set", cancelJobArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. Undraft pawn to allow free autonomous movement/retreat
            var undraftArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "undraft"));
            var undraft = await ExecuteAtomicAsync("pawn.draft.toggle", undraftArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 3. Command pawn to shelter/rest
            var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
            var rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 4. Inject morale stabilization thought to prevent panic breakdown
            var thoughtArgs = BuildArgumentsJson(("pawn_id", pawnId), ("def_name", "Catharsis"));
            var thought = await ExecuteAtomicAsync("pawn.thought.add", thoughtArgs, args, ct).ConfigureAwait(false);

            var summary = BuildStepSummary(
                ("interruptJob", cancelJob),
                ("undraft", undraft),
                ("seekShelter", rest),
                ("stabilizeMorale", thought));

            return Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = summary,
                IsError = rest.IsError
            });
        }
    }
}
