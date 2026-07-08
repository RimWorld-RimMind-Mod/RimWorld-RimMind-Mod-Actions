using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;

namespace RimMind.Actions.Actions
{
    public abstract class CompositeToolCallBase : ICompositeToolCall
    {
        public abstract string Id { get; }

        public string OwnerModId => "RimMindActions";

        public abstract ToolDefinition Definition { get; }

        public abstract IReadOnlyList<string> RequiredToolIds { get; }

        public abstract Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct);

        protected virtual IToolHandler? FindTool(string toolId) => RimMindAPI.Tools.FindById(toolId);

        protected async Task<ToolResult> ExecuteAtomicAsync(
            string toolId,
            string argumentsJson,
            ToolCallArgs parentArgs,
            CancellationToken ct)
        {
            var childCallId = $"{parentArgs.ToolCallId}:{toolId}";
            var handler = FindTool(toolId);
            if (handler == null)
            {
                return ToolResult.Fail($"Required tool not registered: {toolId}", childCallId, toolId);
            }

            var childArgs = new ToolCallArgs
            {
                ToolCallId = childCallId,
                ToolName = toolId,
                ArgumentsJson = argumentsJson,
                PawnId = parentArgs.PawnId,
                NpcId = parentArgs.NpcId,
                TraceId = parentArgs.TraceId,
                Ct = ct
            };

            var result = await handler.ExecuteAsync(childArgs, ct).ConfigureAwait(false);
            return result.IsOk
                ? result.Value with { ToolCallId = childCallId, ToolName = toolId }
                : ToolResult.Fail(result.Error.Message, childCallId, toolId);
        }

        /// <summary>
        /// Tries to read a typed argument from the composite tool's JSON arguments.
        /// Returns false on missing key, null token, or conversion failure.
        /// </summary>
        protected static bool TryGetArgument<T>(string argumentsJson, string key, out T value)
        {
            value = default!;
            if (argumentsJson == null) return false;
            try
            {
                var token = JObject.Parse(argumentsJson)[key];
                if (token == null || token.Type == JTokenType.Null)
                {
                    return false;
                }
                var converted = token.Value<T>();
                if (converted == null)
                {
                    return false;
                }
                value = converted;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        /// <summary>
        /// Builds a compact JSON argument payload for a child atomic tool call.
        /// </summary>
        protected static string BuildArgumentsJson(params (string Key, object? Value)[] pairs)
        {
            var obj = new JObject();
            foreach (var (key, val) in pairs)
            {
                obj[key] = val == null ? JValue.CreateNull() : JToken.FromObject(val);
            }
            return obj.ToString(Formatting.None);
        }

        /// <summary>
        /// Builds the standard per-step summary JSON: each step gets {"ok":bool,"content":string}.
        /// </summary>
        protected static string BuildStepSummary(params (string Name, ToolResult Result)[] steps)
        {
            var obj = new JObject();
            foreach (var (name, result) in steps)
            {
                obj[name] = new JObject
                {
                    ["ok"] = !result.IsError,
                    ["content"] = result.Content
                };
            }
            return obj.ToString(Formatting.None);
        }
    }
}
