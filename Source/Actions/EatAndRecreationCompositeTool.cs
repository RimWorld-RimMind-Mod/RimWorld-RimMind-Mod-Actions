using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool for rehabilitating colonist vital needs (nutrition, energy, and joy).
    /// Cancels active task, orders force rest/recharge, and adds comfort thought to break the downward spiral.
    /// </summary>
    public sealed class EatAndRecreationCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.eat_and_recreation";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Rehabilitate pawn needs: cancels non-urgent jobs, schedules bed rest/recharge, and adds positive relief thought to avoid extreme mental break.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"},\"priority\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"pawn_id\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => new[]
        {
            "pawn.job.set",
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

            // 1. Interrupt any low-priority work holding the pawn back from resting
            var cancelArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "cancel_job"));
            var cancelJob = await ExecuteAtomicAsync("pawn.job.set", cancelArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. Direct pawn to rest and regenerate vital energies
            var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
            var rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 3. Apply catharsis/relief thought
            var thoughtArgs = BuildArgumentsJson(("pawn_id", pawnId), ("def_name", "Catharsis"));
            var thought = await ExecuteAtomicAsync("pawn.thought.add", thoughtArgs, args, ct).ConfigureAwait(false);

            var summary = BuildStepSummary(
                ("interruptJob", cancelJob),
                ("rechargeRest", rest),
                ("applyRelief", thought));

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
