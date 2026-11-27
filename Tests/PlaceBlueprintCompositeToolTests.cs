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
    [Collection("VisualCandidateCache")]
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
            Assert.NotNull(props["target_x"]);
            Assert.NotNull(props["target_z"]);
            Assert.NotNull(props["room_id"]);
            Assert.NotNull(props["candidate_id"]);
            Assert.NotNull(props["auto_construct"]);
            Assert.Contains("actions_inspect_map_view", tool.Definition.Description);

            var decision = tool.Classify(new ToolCallArgs { ToolName = tool.Id, ArgumentsJson = "{\"building_def\":\"Bed\"}" });
            Assert.Equal(AutonomyDomain.BlueprintPlacement, decision.Domain);
        }

        [Fact]
        public async Task PlaceBlueprint_Rejects_Invalid_CandidateId_And_Unpaired_TargetCoords()
        {
            var tool = new PlaceBlueprintCompositeTool();

            var badCandidate = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "bp-cand-0",
                    ToolName = tool.Id,
                    ArgumentsJson = "{\"building_def\":\"Bed\",\"candidate_id\":7}"
                },
                CancellationToken.None);
            Assert.True(badCandidate.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, badCandidate.Error.Code);

            var halfTarget = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "bp-half-target",
                    ToolName = tool.Id,
                    ArgumentsJson = "{\"building_def\":\"Bed\",\"target_x\":12}"
                },
                CancellationToken.None);
            Assert.True(halfTarget.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, halfTarget.Error.Code);
            Assert.Contains("target_x", halfTarget.Error.Message);
        }

        [Fact]
        public async Task PlaceBlueprint_CandidateId_Resolves_Fresh_Cache_And_Rejects_Stale_Cache()
        {
            RimMind.Actions.Spatial.VisualCandidateCache.Clear();
            PlaceBlueprintCompositeTool.TestCurrentTick = 1000;
            try
            {
                var inspectTool = new InspectMapViewCompositeTool();
                var inspectResult = await inspectTool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "inspect-1",
                        ToolName = inspectTool.Id,
                        ArgumentsJson = "{\"building_def\":\"Bed\",\"radius\":10}"
                    },
                    CancellationToken.None);
                Assert.True(inspectResult.IsOk);

                var placeTool = new PlaceBlueprintCompositeTool();
                var freshResult = await placeTool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bp-cand-1",
                        ToolName = placeTool.Id,
                        ArgumentsJson = "{\"building_def\":\"Bed\",\"candidate_id\":1}"
                    },
                    CancellationToken.None);
                Assert.True(freshResult.IsOk);
                Assert.Contains("visual_candidate_1", freshResult.Value.Content);

                // Advance past VisualCandidateCache.MaxFreshnessTicks (1800) -> must reject stale candidate
                PlaceBlueprintCompositeTool.TestCurrentTick = 1000 + RimMind.Actions.Spatial.VisualCandidateCache.MaxFreshnessTicks;
                var staleResult = await placeTool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bp-cand-stale",
                        ToolName = placeTool.Id,
                        ArgumentsJson = "{\"building_def\":\"Bed\",\"candidate_id\":1}"
                    },
                    CancellationToken.None);
                Assert.True(staleResult.IsErr);
                Assert.Equal(RimMindErrorCode.MechanismInvalidAction, staleResult.Error.Code);
                Assert.Contains("actions_inspect_map_view", staleResult.Error.Message);
            }
            finally
            {
                PlaceBlueprintCompositeTool.TestCurrentTick = 1000;
                RimMind.Actions.Spatial.VisualCandidateCache.Clear();
            }
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
