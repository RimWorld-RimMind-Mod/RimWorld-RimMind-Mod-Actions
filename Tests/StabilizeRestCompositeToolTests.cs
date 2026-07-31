using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class StabilizeRestCompositeToolTests : IDisposable
    {
        private readonly IToolRegistry? _originalTools;

        public StabilizeRestCompositeToolTests()
        {
            _originalTools = RimMindAPI.Tools;
        }

        public void Dispose()
        {
            RimMindAPI.Tools = _originalTools!;
        }

        [Fact]
        public async Task ExecuteAsync_InvalidPawnId_Returns_Err_Result()
        {
            var tool = new StabilizeRestCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-1",
                    ToolName = "actions.stabilize_rest",
                    ArgumentsJson = "{\"pawn_id\":\"bad\"}"
                },
                CancellationToken.None);

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
            Assert.Equal("Missing or invalid pawn_id", result.Error.Message);
        }

        [Fact]
        public async Task ExecuteAsync_Calls_Undraft_Then_ForceRest_And_Returns_Summary()
        {
            var registry = new FakeToolRegistry();
            var draft = new RecordingHandler("pawn.draft.toggle", ToolResult.Ok("undrafted"));
            var job = new RecordingHandler("pawn.job.set", ToolResult.Ok("resting"));
            registry.Register(draft);
            registry.Register(job);
            RimMindAPI.Tools = registry;

            var tool = new StabilizeRestCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-2",
                    ToolName = "actions.stabilize_rest",
                    ArgumentsJson = "{\"pawn_id\":123}",
                    NpcId = "npc-1",
                    TraceId = "trace-1"
                },
                CancellationToken.None);

            Assert.True(result.IsOk);
            Assert.False(result.Value.IsError);
            Assert.Equal("parent-2", result.Value.ToolCallId);
            Assert.Equal("actions.stabilize_rest", result.Value.ToolName);
            Assert.Equal(new[] { "pawn.draft.toggle", "pawn.job.set" }, registry.LookupOrder);

            Assert.NotNull(draft.ReceivedArgs);
            Assert.Equal("parent-2:pawn.draft.toggle", draft.ReceivedArgs.ToolCallId);
            Assert.Equal("npc-1", draft.ReceivedArgs.NpcId);
            Assert.Equal("trace-1", draft.ReceivedArgs.TraceId);
            Assert.Equal(123, JObject.Parse(draft.ReceivedArgs.ArgumentsJson)["pawn_id"]!.Value<int>());
            Assert.Equal("undraft", JObject.Parse(draft.ReceivedArgs.ArgumentsJson)["action"]!.Value<string>());

            Assert.NotNull(job.ReceivedArgs);
            Assert.Equal("parent-2:pawn.job.set", job.ReceivedArgs.ToolCallId);
            Assert.Equal(123, JObject.Parse(job.ReceivedArgs.ArgumentsJson)["pawn_id"]!.Value<int>());
            Assert.Equal("force_rest", JObject.Parse(job.ReceivedArgs.ArgumentsJson)["action"]!.Value<string>());

            var summary = JObject.Parse(result.Value.Content);
            Assert.True(summary["undraft"]!["ok"]!.Value<bool>());
            Assert.Equal("undrafted", summary["undraft"]!["content"]!.Value<string>());
            Assert.True(summary["forceRest"]!["ok"]!.Value<bool>());
            Assert.Equal("resting", summary["forceRest"]!["content"]!.Value<string>());
        }

        [Fact]
        public async Task ExecuteAsync_Rest_Error_Marks_Composite_Error()
        {
            var registry = new FakeToolRegistry();
            registry.Register(new RecordingHandler("pawn.draft.toggle", ToolResult.Ok("undrafted")));
            registry.Register(new RecordingHandler("pawn.job.set", ToolResult.Fail("cannot rest")));
            RimMindAPI.Tools = registry;

            var tool = new StabilizeRestCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-3",
                    ToolName = "actions.stabilize_rest",
                    ArgumentsJson = "{\"pawn_id\":321}"
                },
                CancellationToken.None);

            Assert.True(result.IsOk);
            Assert.True(result.Value.IsError);

            var summary = JObject.Parse(result.Value.Content);
            Assert.True(summary["undraft"]!["ok"]!.Value<bool>());
            Assert.False(summary["forceRest"]!["ok"]!.Value<bool>());
            Assert.Equal("cannot rest", summary["forceRest"]!["content"]!.Value<string>());
        }

        [Fact]
        public async Task ExecuteAsync_Cancelled_Between_Steps_Throws_OperationCanceled()
        {
            var registry = new FakeToolRegistry();
            registry.Register(new RecordingHandler("pawn.draft.toggle", ToolResult.Ok("undrafted")));
            registry.Register(new RecordingHandler("pawn.job.set", ToolResult.Ok("resting")));
            RimMindAPI.Tools = registry;

            var tool = new StabilizeRestCompositeTool();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<System.OperationCanceledException>(() => tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-cancel",
                    ToolName = "actions.stabilize_rest",
                    ArgumentsJson = "{\"pawn_id\":7}"
                },
                cts.Token));
        }

        private sealed class FakeToolRegistry : IToolRegistry
        {
            private readonly Dictionary<string, IToolHandler> _handlers = new Dictionary<string, IToolHandler>();
            private readonly List<string> _lookupOrder = new List<string>();

            public IReadOnlyList<string> LookupOrder => _lookupOrder;

            public IReadOnlyList<IToolHandler> All => _handlers.Values.ToList();

            public void Register(IToolHandler handler) => _handlers[handler.Id] = handler;

            public bool Unregister(string toolId) => _handlers.Remove(toolId);

            public IToolHandler? FindById(string toolId)
            {
                _lookupOrder.Add(toolId);
                return _handlers.TryGetValue(toolId, out var handler) ? handler : null;
            }

            public IReadOnlyList<ToolDefinition> GetAllDefinitions() =>
                _handlers.Values.Select(h => h.Definition).ToList();

            public IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind) =>
                _handlers.Values.ToList();

            public IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind) =>
                _handlers.Values.Select(h => h.Definition).ToList();
        }

        private sealed class RecordingHandler : IToolHandler
        {
            private readonly ToolResult _result;

            public RecordingHandler(string id, ToolResult result)
            {
                Id = id;
                _result = result;
            }

            public string Id { get; }

            public string OwnerModId => "RimMindActions.Tests";

            public ToolDefinition Definition => new ToolDefinition { Id = Id };

            public ToolCallArgs? ReceivedArgs { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                ReceivedArgs = args;
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(_result));
            }
        }
    }
}
