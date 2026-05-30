using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation;

namespace RimMind.Actions.Actions
{
    public abstract class CompositeToolCallBase : ICompositeToolCall
    {
        public abstract string Id { get; }

        public string OwnerModId => "RimMindActions";

        public abstract ToolDefinition Definition { get; }

        public abstract IReadOnlyList<string> RequiredToolIds { get; }

        public abstract Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct);

        protected async Task<ToolResult> ExecuteAtomicAsync(
            string toolId,
            string argumentsJson,
            ToolCallArgs parentArgs,
            CancellationToken ct)
        {
            var handler = RimMindAPI.Tools.FindById(toolId);
            if (handler == null)
            {
                return ToolResult.Fail($"Required tool not registered: {toolId}", parentArgs.ToolCallId, toolId);
            }

            var childArgs = new ToolCallArgs
            {
                ToolCallId = $"{parentArgs.ToolCallId}:{toolId}",
                ToolName = toolId,
                ArgumentsJson = argumentsJson,
                NpcId = parentArgs.NpcId,
                TraceId = parentArgs.TraceId,
                Ct = ct
            };

            var result = await handler.ExecuteAsync(childArgs, ct).ConfigureAwait(false);
            return result.IsOk
                ? result.Value with { ToolName = toolId }
                : ToolResult.Fail(result.Error.Message, parentArgs.ToolCallId, toolId);
        }
    }
}
