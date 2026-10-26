using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool for emergency flee & battlefield evacuation.
    /// Cancels risky jobs, undrafts pawn to restore free movement, and forces rest/shelter.
    /// </summary>
    public sealed class EmergencyFleeCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.emergency_flee";

        private static readonly string[] RequiredTools = new[]
        {
            "pawn.job.set",
            "pawn.draft.toggle"
        };

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Emergency flee and evacuation: interrupts current risky work, undrafts pawn if stuck, and forces safe rest/shelter.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"},\"danger_level\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"pawn_id\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => RequiredTools;

        public override async Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            var pawnIdResult = TryGetPawnIdOrError(args, "pawn_id");
            if (pawnIdResult.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(pawnIdResult.Error);
            }
            int pawnId = pawnIdResult.Value;

            var guard = GuardColonistTarget(pawnId, AutonomyDomain.EmergencySurvival, args);
            if (guard.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(guard.Error);
            }

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

            // No mood thought is injected: granting Catharsis on every LLM call was a free, repeatable mood buff.
            var summary = BuildStepSummary(
                ("interruptJob", cancelJob),
                ("undraft", undraft),
                ("seekShelter", rest));

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
