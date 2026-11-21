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

        private static readonly string[] Aliases = new[]
        {
            "eat_and_recreation",
            "calm_recreation",
            "actions_eat_and_recreation",
            "actions.calm_recreation",
            "actions_calm_recreation"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "priority",
            "reason"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.EmergencySurvival;
        protected override ToolScope? ToolScopes => ToolScope.ColonistSelf;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => true;
        protected override string? DisplayName => "Eat & Recreation";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

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
            var argsRoot = TryParseArgumentsObject(args.ArgumentsJson);
            var pawnIdResult = TryGetPawnIdOrError(argsRoot, args, "pawn_id");
            if (pawnIdResult.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(pawnIdResult.Error);
            }
            int pawnId = pawnIdResult.Value;

            var domain = ResolveAutonomyDomain(argsRoot, args)
                ?? AutonomyDomain.EmergencySurvival;
            var guard = GuardColonistTarget(pawnId, domain, args);
            if (guard.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(guard.Error);
            }

            // 1. Interrupt any low-priority work holding the pawn back from resting
            var cancelArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "cancel_job"));
            var cancelJob = await ExecuteAtomicAsync("pawn.job.set", cancelArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. Direct pawn to eat food if hunger-driven, otherwise rest and regenerate vital energies
            TryGetArgument<string>(argsRoot, "reason", out var reasonText);
            bool wantsFood = !string.IsNullOrWhiteSpace(reasonText) &&
                (reasonText.IndexOf("eat", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 reasonText.IndexOf("food", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 reasonText.IndexOf("hunger", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 reasonText.IndexOf("starv", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 reasonText.IndexOf("meal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 reasonText.IndexOf("nutrition", System.StringComparison.OrdinalIgnoreCase) >= 0);
#if !RIMMIND_ACTIONS_TESTS
            var targetPawn = RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (targetPawn?.needs?.food != null && targetPawn.needs.food.CurLevelPercentage < 0.80f)
            {
                wantsFood = true;
            }
#endif

            ToolResult rest;
            if (wantsFood)
            {
                var eatArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "eat_food"));
                rest = await ExecuteAtomicAsync("pawn.job.set", eatArgs, args, ct).ConfigureAwait(false);
                if (rest.IsError)
                {
                    var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
                    rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct).ConfigureAwait(false);
                }
            }
            else
            {
                var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
                rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct).ConfigureAwait(false);
            }

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
