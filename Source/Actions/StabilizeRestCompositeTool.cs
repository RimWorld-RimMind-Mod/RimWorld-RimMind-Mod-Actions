using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    public sealed class StabilizeRestCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.stabilize_rest";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Undraft pawn if possible, then force rest.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"pawn_id\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => new[]
        {
            "pawn.draft.toggle",
            "pawn.job.set"
        };

        public override async Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            if (!TryGetArgument(args.ArgumentsJson, "pawn_id", out int pawnId))
            {
                return Result<ToolResult, RimMindError>.Err(
                    new RimMindError(RimMindErrorCode.MechanismInvalidAction, "Missing or invalid pawn_id")
                    {
                        TraceId = args.TraceId
                    });
            }

            var undraftArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "undraft"));
            var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));

            var undraft = await ExecuteAtomicAsync("pawn.draft.toggle", undraftArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct).ConfigureAwait(false);

            // "Undraft pawn if possible" — undraft is best-effort: its failure does NOT fail
            // the composite. rest is the authoritative step; IsError follows rest.
            var summary = BuildStepSummary(("undraft", undraft), ("forceRest", rest));

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
