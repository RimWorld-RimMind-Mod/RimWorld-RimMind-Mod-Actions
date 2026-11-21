using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool for battlefield patient triage & immediate medical stabilization.
    /// Orders injured patient to emergency rest and frees up an assigned doctor from trivial work.
    /// </summary>
    public sealed class TriagePatientCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.triage_patient";

        private static readonly string[] RequiredTools = new[]
        {
            "pawn.job.set"
        };

        private static readonly string[] Aliases = new[]
        {
            "triage_patient",
            "actions_triage_patient"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "urgency",
            "reason"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.EmergencySurvival;
        protected override ToolScope? ToolScopes => ToolScope.ColonistSelf;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => true;
        protected override string? DisplayName => "Triage Patient";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Battlefield triage: directs patient to emergency rest/triage and interrupts doctor if assigned to prioritize medical rescue.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"patient_id\":{\"type\":\"integer\"},\"doctor_id\":{\"type\":\"integer\"},\"urgency\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"patient_id\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => RequiredTools;

        public override async Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            var argsRoot = TryParseArgumentsObject(args.ArgumentsJson);
            var patientIdResult = TryGetPawnIdOrError(argsRoot, args, "patient_id");
            if (patientIdResult.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(patientIdResult.Error);
            }
            int patientId = patientIdResult.Value;

            var domain = ResolveAutonomyDomain(argsRoot, args)
                ?? AutonomyDomain.EmergencySurvival;
            var patientGuard = GuardColonistTarget(patientId, domain, args, "patient_id");
            if (patientGuard.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(patientGuard.Error);
            }

            int doctorId = 0;
            bool hasDoctor = TryGetArgument<int>(argsRoot, "doctor_id", out doctorId) && doctorId > 0;
            if (hasDoctor)
            {
                var doctorGuard = GuardColonistTarget(doctorId, domain, args, "doctor_id");
                if (doctorGuard.IsErr)
                {
                    return Result<ToolResult, RimMindError>.Err(doctorGuard.Error);
                }
            }

            // 1. Direct patient to emergency bed rest
            var patientRestArgs = BuildArgumentsJson(("pawn_id", patientId), ("action", "force_rest"));
            var patientRest = await ExecuteAtomicAsync("pawn.job.set", patientRestArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. If doctor_id is provided (or auto-resolved on the map), direct doctor to rescue/tend patient immediately
            ToolResult doctorPrep = ToolResult.Ok("no doctor specified");
#if !RIMMIND_ACTIONS_TESTS
            var patientPawn = RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(patientId);
            if ((!hasDoctor || doctorId == patientId) && patientPawn?.Map != null)
            {
                var candidates = RimMind.Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(patientPawn.Map);
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if (c == null || c.thingIDNumber == patientId || c.Dead || c.Downed) continue;
                    if (c.WorkTypeIsDisabled(RimWorld.WorkTypeDefOf.Doctor)) continue;
                    doctorId = c.thingIDNumber;
                    hasDoctor = true;
                    break;
                }
            }
#endif
            if (hasDoctor)
            {
#if !RIMMIND_ACTIONS_TESTS
                // Respect player work priorities: never mutate workSettings.SetPriority; dispatch direct rescue/tend job.
                if (patientPawn != null && patientPawn.Downed && !RimWorld.RestUtility.InBed(patientPawn))
                {
                    var rescueArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "rescue_pawn"), ("target_pawn_id", patientId));
                    doctorPrep = await ExecuteAtomicAsync("pawn.job.set", rescueArgs, args, ct).ConfigureAwait(false);
                }
                else if (patientPawn?.health != null && patientPawn.health.HasHediffsNeedingTend())
                {
                    var tendArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "tend_pawn"), ("target_pawn_id", patientId));
                    doctorPrep = await ExecuteAtomicAsync("pawn.job.set", tendArgs, args, ct).ConfigureAwait(false);
                }
                else
                {
                    var doctorCancelArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "cancel_job"));
                    doctorPrep = await ExecuteAtomicAsync("pawn.job.set", doctorCancelArgs, args, ct).ConfigureAwait(false);
                }

                if (doctorPrep.IsError)
                {
                    var doctorCancelArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "cancel_job"));
                    doctorPrep = await ExecuteAtomicAsync("pawn.job.set", doctorCancelArgs, args, ct).ConfigureAwait(false);
                }
#else
                var doctorCancelArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "cancel_job"));
                doctorPrep = await ExecuteAtomicAsync("pawn.job.set", doctorCancelArgs, args, ct).ConfigureAwait(false);
#endif
            }

            // No mood thought is injected: granting Catharsis on every LLM call was a free, repeatable mood buff.
            var summary = BuildStepSummary(
                ("patientBedRest", patientRest),
                ("doctorTriageReady", doctorPrep));

            bool noCareAvailable = false;
#if !RIMMIND_ACTIONS_TESTS
            // Honest outcome: a downed patient with no able doctor gets no care at all
            // (a standing patient can still self-tend through vanilla work).
            noCareAvailable = !hasDoctor && patientPawn != null && patientPawn.Downed;
            if (noCareAvailable)
            {
                summary = $"No able-bodied colonist can rescue or tend {patientPawn!.LabelShort}; no medical care was given. {summary}";
            }
#endif

            return Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = summary,
                IsError = patientRest.IsError || noCareAvailable
            });
        }
    }
}
