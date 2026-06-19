using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            if (!TryGetPawnId(args.ArgumentsJson, out var pawnId))
            {
                return Result<ToolResult, RimMindError>.Err(
                    new RimMindError(RimMindErrorCode.MechanismInvalidAction, "Missing or invalid pawn_id")
                    {
                        TraceId = args.TraceId
                    });
            }

            var undraftArgs = new JObject
            {
                ["pawn_id"] = pawnId,
                ["action"] = "undraft"
            }.ToString(Formatting.None);

            var restArgs = new JObject
            {
                ["pawn_id"] = pawnId,
                ["action"] = "force_rest"
            }.ToString(Formatting.None);

            var undraft = await ExecuteAtomicAsync(
                "pawn.draft.toggle",
                undraftArgs,
                args,
                ct).ConfigureAwait(false);
            var rest = await ExecuteAtomicAsync(
                "pawn.job.set",
                restArgs,
                args,
                ct).ConfigureAwait(false);

            var summary = new JObject
            {
                ["undraft"] = new JObject
                {
                    ["ok"] = !undraft.IsError,
                    ["content"] = undraft.Content
                },
                ["forceRest"] = new JObject
                {
                    ["ok"] = !rest.IsError,
                    ["content"] = rest.Content
                }
            }.ToString(Formatting.None);

            return Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = summary,
                IsError = rest.IsError
            });
        }

        private static bool TryGetPawnId(string argumentsJson, out int pawnId)
        {
            pawnId = 0;

            try
            {
                var json = JObject.Parse(argumentsJson);
                var token = json["pawn_id"];
                if (token == null || token.Type != JTokenType.Integer)
                {
                    return false;
                }

                return int.TryParse(token.ToString(), out pawnId);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
