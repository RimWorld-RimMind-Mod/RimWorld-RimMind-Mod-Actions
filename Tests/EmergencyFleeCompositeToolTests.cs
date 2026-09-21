using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class EmergencyFleeCompositeToolTests : IDisposable
    {
        private readonly IToolRegistry? _originalTools;

        public EmergencyFleeCompositeToolTests()
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
            var tool = new EmergencyFleeCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-1",
                    ToolName = "actions.emergency_flee",
                    ArgumentsJson = "{\"pawn_id\":\"not_an_int\"}"
                },
                CancellationToken.None);

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
            Assert.Equal("Missing or invalid pawn_id", result.Error.Message);
        }

        [Fact]
        public async Task ExecuteAsync_ExecutesAllPipelineSteps_And_ReturnsSummary()
        {
            var registry = new FakeToolRegistry();
            var job = new RecordingHandler("pawn.job.set", ToolResult.Ok("ok"));
            var draft = new RecordingHandler("pawn.draft.toggle", ToolResult.Ok("undrafted"));
            var thought = new RecordingHandler("pawn.thought.add", ToolResult.Ok("thought_added"));
            registry.Register(job);
            registry.Register(draft);
            registry.Register(thought);
            RimMindAPI.Tools = registry;

            var tool = new EmergencyFleeCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-2",
                    ToolName = "actions.emergency_flee",
                    ArgumentsJson = "{\"pawn_id\":42,\"danger_level\":\"Extreme\"}"
                },
                CancellationToken.None);

            Assert.True(result.IsOk);
            var parsed = JObject.Parse(result.Value.Content);
            Assert.True((bool)parsed["seekShelter"]!["ok"]!);
            Assert.True((bool)parsed["stabilizeMorale"]!["ok"]!);
        }

        [Fact]
        public void Definition_And_RequiredToolIds_AreAccurate()
        {
            var tool = new EmergencyFleeCompositeTool();
            Assert.Equal("actions.emergency_flee", tool.Id);
            Assert.Equal("composite", tool.Definition.Category);
            Assert.Contains("pawn.job.set", tool.RequiredToolIds);
            Assert.Contains("pawn.draft.toggle", tool.RequiredToolIds);
            Assert.Contains("pawn.thought.add", tool.RequiredToolIds);
        }
    }
}
