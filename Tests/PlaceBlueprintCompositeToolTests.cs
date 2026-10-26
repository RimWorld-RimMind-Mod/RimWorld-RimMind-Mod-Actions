using System;
using System.IO;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class PlaceBlueprintCompositeToolTests
    {
        private static readonly string RepoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string ReadActionsSource(string relativePath)
        {
            var path = Path.Combine(RepoRoot, "RimMind-Actions", "Source", relativePath);
            Assert.True(File.Exists(path), $"Missing source file: {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void PlaceBlueprint_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlaceBlueprintCompositeTool.cs"));
            Assert.Contains("public sealed class PlaceBlueprintCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_place_blueprint\";", source);
        }

        [Fact]
        public void PlaceBlueprint_Declares_Parameters_Schema_With_AutoStuff_And_IntentLease()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlaceBlueprintCompositeTool.cs"));
            Assert.Contains("\"building_def\"", source);
            Assert.Contains("\"stuff_def\"", source);
            Assert.Contains("\"location_hint\"", source);
            Assert.Contains("\"auto_construct\"", source);
        }

        [Fact]
        public void PlaceBlueprint_Handles_Missing_BuildingDef_Validation()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlaceBlueprintCompositeTool.cs"));
            Assert.Contains("string.IsNullOrWhiteSpace(buildingDefName)", source);
            Assert.Contains("RimMindErrors.InvalidAction", source);
            Assert.Contains("Missing required parameter 'building_def'", source);
        }

        [Fact]
        public void PlaceBlueprint_Integrates_AgentIntentSession_Chaining()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlaceBlueprintCompositeTool.cs"));
            Assert.Contains("RimMindAPI.Agent.StartIntentSession", source);
            Assert.DoesNotContain("using RimMind.Infrastructure", source);
            Assert.Contains("IssueConstructionSubJob(p, map, blueprint, targetCell)", source);
            Assert.Contains("targetCell.GetThingList(map).OfType<Frame>().Any(f => !f.Destroyed && f.Spawned)", source);
            Assert.Contains("WorkGiver_ConstructDeliverResourcesToFrames", source);
            Assert.Contains("StartIntentSession", source);
        }
    }
}
