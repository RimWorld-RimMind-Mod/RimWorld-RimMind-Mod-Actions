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
            var patientIdResult = TryGetPawnIdOrError(args, "patient_id");
            if (patientIdResult.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(patientIdResult.Error);
            }
            int patientId = patientIdResult.Value;

            var patientGuard = GuardColonistTarget(patientId, AutonomyDomain.EmergencySurvival, args, "patient_id");
            if (patientGuard.IsErr)
            {
                return Result<ToolResult, RimMindError>.Err(patientGuard.Error);
            }

            int doctorId = 0;
            bool hasDoctor = TryGetArgument<int>(args.ArgumentsJson, "doctor_id", out doctorId) && doctorId > 0;
            if (hasDoctor)
            {
                var doctorGuard = GuardColonistTarget(doctorId, AutonomyDomain.EmergencySurvival, args, "doctor_id");
                if (doctorGuard.IsErr)
                {
                    return Result<ToolResult, RimMindError>.Err(doctorGuard.Error);
                }
            }

            // 1. Direct patient to emergency bed rest
            var patientRestArgs = BuildArgumentsJson(("pawn_id", patientId), ("action", "force_rest"));
            var patientRest = await ExecuteAtomicAsync("pawn.job.set", patientRestArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. If doctor_id is provided, interrupt doctor's non-medical work so they can triage immediately
            ToolResult doctorPrep = ToolResult.Ok("no doctor specified");
            if (hasDoctor)
            {
                var doctorCancelArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "cancel_job"));
                doctorPrep = await ExecuteAtomicAsync("pawn.job.set", doctorCancelArgs, args, ct).ConfigureAwait(false);
            }

            // No mood thought is injected: granting Catharsis on every LLM call was a free, repeatable mood buff.
            var summary = BuildStepSummary(
                ("patientBedRest", patientRest),
                ("doctorTriageReady", doctorPrep));

            return Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = summary,
                IsError = patientRest.IsError
            });
        }
    }
}
