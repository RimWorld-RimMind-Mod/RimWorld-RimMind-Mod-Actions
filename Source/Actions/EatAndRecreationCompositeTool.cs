using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool for rehabilitating colonist vital needs (nutrition, energy, and joy).
    /// Cancels active task and orders force rest/recharge to break the downward spiral.
    /// </summary>
    public sealed class EatAndRecreationCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.eat_and_recreation";

        private static readonly string[] RequiredTools = new[]
        {
            "pawn.job.set"
        };

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Rehabilitate pawn needs: cancels non-urgent jobs and schedules bed rest/recharge so vanilla need-seeking can take over.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"},\"priority\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"pawn_id\"]}"
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

            // 1. Interrupt any low-priority work holding the pawn back from resting
            var cancelArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "cancel_job"));
            var cancelJob = await ExecuteAtomicAsync("pawn.job.set", cancelArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. Direct pawn to rest and regenerate vital energies
            var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
            var rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct).ConfigureAwait(false);

            // No mood thought is injected: granting Catharsis on every LLM call was a free, repeatable mood buff.
            var summary = BuildStepSummary(
                ("interruptJob", cancelJob),
                ("rechargeRest", rest));

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
