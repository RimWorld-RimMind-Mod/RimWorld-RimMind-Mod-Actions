using System;
using System.IO;
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
    public class PlaceBlueprintCompositeToolTests
    {
        private static readonly string RepoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        [Fact]
        public void PlaceBlueprint_Declares_Metadata_And_Parameters_Schema()
        {
            var tool = new PlaceBlueprintCompositeTool();
            Assert.Equal("actions_place_blueprint", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            var props = schema["properties"] as JObject;
            Assert.NotNull(props);
            Assert.NotNull(props!["building_def"]);
            Assert.NotNull(props["stuff_def"]);
            Assert.NotNull(props["location_hint"]);
            Assert.NotNull(props["auto_construct"]);

            var decision = tool.Classify(new ToolCallArgs { ToolName = tool.Id, ArgumentsJson = "{\"building_def\":\"Bed\"}" });
            Assert.Equal(AutonomyDomain.BlueprintPlacement, decision.Domain);
        }

        [Fact]
        public async Task PlaceBlueprint_Missing_BuildingDef_Returns_InvalidAction_With_TraceId()
        {
            var tool = new PlaceBlueprintCompositeTool();
            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "bp-1",
                    ToolName = tool.Id,
                    ArgumentsJson = "{}",
                    TraceId = "trace-bp-missing"
                },
                CancellationToken.None);

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
            Assert.Contains("building_def", result.Error.Message);
            Assert.Equal("trace-bp-missing", result.Error.TraceId);
        }

        [Fact]
        public async Task PlaceBlueprint_Rejects_OffMainThread_And_Enforces_ColonistGuard()
        {
            var tool = new PlaceBlueprintCompositeTool();
            try
            {
                Verse.UnityData.IsInMainThread = false;
                var offThread = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bp-thread",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"building_def\":\"Bed\"}",
                        TraceId = "trace-bp-thread"
                    },
                    CancellationToken.None);

                Assert.True(offThread.IsErr);
                Assert.Equal(RimMindErrorCode.ToolExecutionFailed, offThread.Error.Code);
                Assert.Equal("trace-bp-thread", offThread.Error.TraceId);

                Verse.UnityData.IsInMainThread = true;
                CompositeToolCallBase.TestColonistGuard = (_, domain, _) =>
                    Result<Unit, RimMindError>.Err(RimMindErrors.ToolPolicyDenied(tool.Id, $"denied:{domain}"));

                var denied = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bp-denied",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"building_def\":\"Bed\",\"pawn_id\":42}"
                    },
                    CancellationToken.None);

                Assert.True(denied.IsErr);
                Assert.Equal(RimMindErrorCode.ToolPolicyDenied, denied.Error.Code);
                Assert.Contains("BlueprintPlacement", denied.Error.Message);
            }
            finally
            {
                Verse.UnityData.IsInMainThread = true;
                CompositeToolCallBase.TestColonistGuard = null;
            }
        }

        [Fact]
        public void AllCompositeTools_Use_NullSafe_DefName_And_Label_Comparisons()
        {
            var actionsDir = Path.Combine(RepoRoot, "RimMind-Actions", "Source", "Actions");
            foreach (var file in Directory.GetFiles(actionsDir, "*CompositeTool.cs"))
            {
                var source = File.ReadAllText(file);
                Assert.DoesNotContain(".label.Equals(", source);
                Assert.DoesNotContain(".defName.Equals(", source);
            }
        }
    }
}
