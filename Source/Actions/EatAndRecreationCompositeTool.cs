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
            Description = "Rehabilitate pawn needs: cancels non-urgent jobs and orders eating or bed rest/recharge based on explicit mode or pawn hunger.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"},\"mode\":{\"type\":\"string\",\"enum\":[\"auto\",\"eat\",\"rest\"],\"description\":\"Recovery mode: 'eat' to consume food, 'rest' to force bed rest, or 'auto' (default: eat if hungry, otherwise rest).\"},\"priority\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"pawn_id\"]}"
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

            // 1. Interrupt any low-priority work holding the pawn back from recovering
            var cancelArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "cancel_job"));
            var cancelJob = await ExecuteAtomicAsync("pawn.job.set", cancelArgs, args, ct);
            ct.ThrowIfCancellationRequested();

            // 2. Decide eat vs rest from explicit schema parameter `mode` (or live colonist hunger in `auto` mode)
            TryGetArgument<string>(argsRoot, "mode", out var modeRaw);
            string mode = string.IsNullOrWhiteSpace(modeRaw) ? "auto" : modeRaw!.Trim().ToLowerInvariant();
            bool wantsFood = mode == "eat";
#if !RIMMIND_ACTIONS_TESTS
            if (mode == "auto")
            {
                var targetPawn = RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
                if (targetPawn?.needs?.food != null && targetPawn.needs.food.CurLevelPercentage < 0.80f)
                {
                    wantsFood = true;
                }
            }
#endif

            ToolResult rest;
            if (wantsFood)
            {
                var eatArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "eat_food"));
                rest = await ExecuteAtomicAsync("pawn.job.set", eatArgs, args, ct);
                if (rest.IsError)
                {
                    ct.ThrowIfCancellationRequested();
                    var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
                    rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct);
                }
            }
            else
            {
                var restArgs = BuildArgumentsJson(("pawn_id", pawnId), ("action", "force_rest"));
                rest = await ExecuteAtomicAsync("pawn.job.set", restArgs, args, ct);
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
