using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool for battlefield patient triage & immediate medical stabilization.
    /// Orders injured patient to emergency rest, frees up an assigned doctor from trivial work, and buffers pain/panic with morale support.
    /// </summary>
    public sealed class TriagePatientCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.triage_patient";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "composite",
            Description = "Battlefield triage: directs patient to emergency rest/triage, interrupts doctor if assigned to prioritize medical rescue, and injects morale thought.",
            ParametersSchema =
                "{\"type\":\"object\",\"properties\":{\"patient_id\":{\"type\":\"integer\"},\"doctor_id\":{\"type\":\"integer\"},\"urgency\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"patient_id\"]}"
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
            int patientId = 0;
            if (!TryGetArgument<int>(args.ArgumentsJson, "patient_id", out patientId) || patientId <= 0)
            {
                if (!TryGetArgument<int>(args.ArgumentsJson, "pawn_id", out patientId) || patientId <= 0)
                {
                    return Result<ToolResult, RimMindError>.Err(
                        new RimMindError(RimMindErrorCode.MechanismInvalidAction, "Missing or invalid patient_id")
                        {
                            TraceId = args.TraceId
                        });
                }
            }

            // 1. Direct patient to emergency bed rest
            var patientRestArgs = BuildArgumentsJson(("pawn_id", patientId), ("action", "force_rest"));
            var patientRest = await ExecuteAtomicAsync("pawn.job.set", patientRestArgs, args, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. If doctor_id is provided, interrupt doctor's non-medical work so they can triage immediately
            ToolResult doctorPrep = ToolResult.Ok("no doctor specified");
            if (TryGetArgument<int>(args.ArgumentsJson, "doctor_id", out int doctorId) && doctorId > 0)
            {
                var doctorCancelArgs = BuildArgumentsJson(("pawn_id", doctorId), ("action", "cancel_job"));
                doctorPrep = await ExecuteAtomicAsync("pawn.job.set", doctorCancelArgs, args, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }

            // 3. Apply patient pain buffer thought (Catharsis) to avoid shock breakdown
            var thoughtArgs = BuildArgumentsJson(("pawn_id", patientId), ("def_name", "Catharsis"));
            var thought = await ExecuteAtomicAsync("pawn.thought.add", thoughtArgs, args, ct).ConfigureAwait(false);

            var summary = BuildStepSummary(
                ("patientBedRest", patientRest),
                ("doctorTriageReady", doctorPrep),
                ("stabilizeShock", thought));

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
