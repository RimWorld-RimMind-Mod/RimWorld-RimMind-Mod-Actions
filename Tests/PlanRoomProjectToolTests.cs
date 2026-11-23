using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class PlanRoomProjectToolTests
    {
        [Fact]
        public void PlanRoomProject_Declares_Metadata_And_Parameters_Schema()
        {
            var tool = new PlanRoomProjectCompositeTool();
            Assert.Equal("actions_plan_room_project", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            var props = schema["properties"] as JObject;
            Assert.NotNull(props);
            Assert.NotNull(props!["room_type"]);
            Assert.NotNull(props["size"]);
            Assert.NotNull(props["stuff_def"]);
            Assert.NotNull(props["pawn_id"]);
            Assert.NotNull(props["include_bed"]);

            var decision = tool.Classify(new ToolCallArgs { ToolName = tool.Id, ArgumentsJson = "{\"room_type\":\"Shelter\"}" });
            Assert.Equal(AutonomyDomain.BlueprintPlacement, decision.Domain);
        }

        [Fact]
        public async Task PlanRoomProject_Enforces_MainThread_And_BlueprintPlacement_Guard()
        {
            var tool = new PlanRoomProjectCompositeTool();
            try
            {
                Verse.UnityData.IsInMainThread = false;
                var offThread = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "room-thread",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"room_type\":\"Bedroom\"}",
                        TraceId = "trace-room-thread"
                    },
                    CancellationToken.None);

                Assert.True(offThread.IsErr);
                Assert.Equal(RimMindErrorCode.ToolExecutionFailed, offThread.Error.Code);
                Assert.Equal("trace-room-thread", offThread.Error.TraceId);

                Verse.UnityData.IsInMainThread = true;
                AutonomyDomain? capturedDomain = null;
                CompositeToolCallBase.TestColonistGuard = (_, domain, _) =>
                {
                    capturedDomain = domain;
                    return Result<Unit, RimMindError>.Err(RimMindErrors.ToolPolicyDenied(tool.Id, "approval required"));
                };

                var denied = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "room-guard",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"room_type\":\"Bedroom\",\"pawn_id\":11}"
                    },
                    CancellationToken.None);

                Assert.True(denied.IsErr);
                Assert.Equal(AutonomyDomain.BlueprintPlacement, capturedDomain);
                Assert.Equal(RimMindErrorCode.ToolPolicyDenied, denied.Error.Code);

                CompositeToolCallBase.TestColonistGuard = null;
                var ok = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "room-ok",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"room_type\":\"Bedroom\",\"size\":5}"
                    },
                    CancellationToken.None);

                Assert.True(ok.IsOk);
                Assert.False(ok.Value.IsError);
            }
            finally
            {
                Verse.UnityData.IsInMainThread = true;
                CompositeToolCallBase.TestColonistGuard = null;
            }
        }
    }
}
