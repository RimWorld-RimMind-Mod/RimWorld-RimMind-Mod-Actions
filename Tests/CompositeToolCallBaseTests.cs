using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class CompositeToolCallBaseTests
    {
        [Fact]
        public async Task ExecuteAtomicAsync_Passes_Child_Args_And_Normalizes_Success_Result()
        {
            using var cts = new CancellationTokenSource();
            var handler = new CapturingToolHandler(
                Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("done", "handler-id", "handler-name")));
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>
            {
                ["move_to"] = handler
            });
            var parentArgs = new ToolCallArgs
            {
                ToolCallId = "parent-1",
                ToolName = "composite",
                ArgumentsJson = "{\"ignored\":true}",
                NpcId = "npc-7",
                TraceId = "trace-9",
                Ct = CancellationToken.None
            };

            var result = await composite.ExecuteAtomicForTestAsync(
                "move_to",
                "{\"x\":1}",
                parentArgs,
                cts.Token);

            Assert.False(result.IsError);
            Assert.Equal("done", result.Content);
            Assert.Equal("parent-1:move_to", result.ToolCallId);
            Assert.Equal("move_to", result.ToolName);
            Assert.NotNull(handler.ReceivedArgs);
            Assert.Equal("parent-1:move_to", handler.ReceivedArgs.ToolCallId);
            Assert.Equal("move_to", handler.ReceivedArgs.ToolName);
            Assert.Equal("{\"x\":1}", handler.ReceivedArgs.ArgumentsJson);
            Assert.Equal("npc-7", handler.ReceivedArgs.NpcId);
            Assert.Equal("trace-9", handler.ReceivedArgs.TraceId);
            Assert.Equal(cts.Token, handler.ReceivedArgs.Ct);
            Assert.Equal(cts.Token, handler.ReceivedCancellationToken);
        }

        [Fact]
        public async Task ExecuteAtomicAsync_Missing_Handler_Returns_Error_With_Child_Id_And_Tool_Name()
        {
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>());
            var parentArgs = new ToolCallArgs { ToolCallId = "parent-2" };

            var result = await composite.ExecuteAtomicForTestAsync(
                "equip",
                "{}",
                parentArgs,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("Required tool not registered: equip", result.Content);
            Assert.Equal("parent-2:equip", result.ToolCallId);
            Assert.Equal("equip", result.ToolName);
        }

        [Fact]
        public async Task ExecuteAtomicAsync_Handler_Error_Returns_Error_With_Child_Id_And_Tool_Name()
        {
            var handler = new CapturingToolHandler(
                Result<ToolResult, RimMindError>.Err(
                    new RimMindError(RimMindErrorCode.ToolExecutionFailed, "tool failed")));
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>
            {
                ["haul"] = handler
            });
            var parentArgs = new ToolCallArgs { ToolCallId = "parent-3" };

            var result = await composite.ExecuteAtomicForTestAsync(
                "haul",
                "{}",
                parentArgs,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("tool failed", result.Content);
            Assert.Equal("parent-3:haul", result.ToolCallId);
            Assert.Equal("haul", result.ToolName);
        }

        private sealed class TestCompositeToolCall : CompositeToolCallBase
        {
            private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;

            public TestCompositeToolCall(IReadOnlyDictionary<string, IToolHandler> handlers)
            {
                _handlers = handlers;
            }

            public override string Id => "test_composite";

            public override ToolDefinition Definition => new ToolDefinition { Id = Id };

            public override IReadOnlyList<string> RequiredToolIds => new[] { "test_tool" };

            public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
                ToolCallArgs args,
                CancellationToken ct) =>
                Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("unused")));

            public Task<ToolResult> ExecuteAtomicForTestAsync(
                string toolId,
                string argumentsJson,
                ToolCallArgs parentArgs,
                CancellationToken ct) =>
                ExecuteAtomicAsync(toolId, argumentsJson, parentArgs, ct);

            protected override IToolHandler? FindTool(string toolId) =>
                _handlers.TryGetValue(toolId, out var handler) ? handler : null;
        }

        private sealed class CapturingToolHandler : IToolHandler
        {
            private readonly Result<ToolResult, RimMindError> _result;

            public CapturingToolHandler(Result<ToolResult, RimMindError> result)
            {
                _result = result;
            }

            public string Id => "capturing_tool";

            public string OwnerModId => "RimMindActions.Tests";

            public ToolDefinition Definition => new ToolDefinition { Id = Id };

            public ToolCallArgs? ReceivedArgs { get; private set; }

            public CancellationToken ReceivedCancellationToken { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                ReceivedArgs = args;
                ReceivedCancellationToken = ct;
                return Task.FromResult(_result);
            }
        }
    }
}
