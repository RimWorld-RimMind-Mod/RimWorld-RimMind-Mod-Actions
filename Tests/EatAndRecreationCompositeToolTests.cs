using System;
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
    public class EatAndRecreationCompositeToolTests : IDisposable
    {
        private readonly IToolRegistry? _originalTools;

        public EatAndRecreationCompositeToolTests()
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
            var tool = new EatAndRecreationCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-eat-1",
                    ToolName = "actions.eat_and_recreation",
                    ArgumentsJson = "{}"
                },
                CancellationToken.None);

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
        }

        [Fact]
        public async Task ExecuteAsync_SchedulesRest_And_Relief_Successfully()
        {
            var registry = new FakeToolRegistry();
            var job = new RecordingHandler("pawn.job.set", ToolResult.Ok("ok"));
            var thought = new RecordingHandler("pawn.thought.add", ToolResult.Ok("thought_added"));
            registry.Register(job);
            registry.Register(thought);
            RimMindAPI.Tools = registry;

            var tool = new EatAndRecreationCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-eat-2",
                    ToolName = "actions.eat_and_recreation",
                    ArgumentsJson = "{\"pawn_id\":100,\"priority\":\"High\"}"
                },
                CancellationToken.None);

            Assert.True(result.IsOk);
            var parsed = JObject.Parse(result.Value.Content);
            Assert.True((bool)parsed["interruptJob"]!["ok"]!);
            Assert.True((bool)parsed["rechargeRest"]!["ok"]!);
            Assert.True((bool)parsed["applyRelief"]!["ok"]!);
        }

        [Fact]
        public void Definition_And_RequiredToolIds_AreAccurate()
        {
            var tool = new EatAndRecreationCompositeTool();
            Assert.Equal("actions.eat_and_recreation", tool.Id);
            Assert.Equal("composite", tool.Definition.Category);
            Assert.Contains("pawn.job.set", tool.RequiredToolIds);
            Assert.Contains("pawn.thought.add", tool.RequiredToolIds);
        }
    }
}
