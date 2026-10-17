using System;
using System.IO;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class PlanRoomProjectToolTests
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
        public void PlanRoomProject_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlanRoomProjectCompositeTool.cs"));
            Assert.Contains("public sealed class PlanRoomProjectCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_plan_room_project\";", source);
        }

        [Fact]
        public void PlanRoomProject_Declares_Parameters_Schema()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlanRoomProjectCompositeTool.cs"));
            Assert.Contains("\"room_type\"", source);
            Assert.Contains("\"size\"", source);
            Assert.Contains("\"stuff_def\"", source);
            Assert.Contains("\"pawn_id\"", source);
            Assert.Contains("\"include_bed\"", source);
        }

        [Fact]
        public void PlanRoomProject_Integrates_BlueprintPlacement_Autonomy_Guard()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlanRoomProjectCompositeTool.cs"));
            Assert.Contains("ActionPermissionGuard.CheckPermission", source);
            Assert.Contains("AutonomyDomain.BlueprintPlacement", source);
        }

        [Fact]
        public void PlanRoomProject_Integrates_ResourceSafetyGuard()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlanRoomProjectCompositeTool.cs"));
            Assert.Contains("ResourceSafetyGuard.CheckConstructionSafety", source);
        }

        [Fact]
        public void PlanRoomProject_Chains_AgentIntentSession()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlanRoomProjectCompositeTool.cs"));
            Assert.Contains("AgentIntentSession", source);
            Assert.Contains("StartIntentSession", source);
            Assert.Contains("PlaceBlueprintForBuild", source);
            Assert.Contains("targetCells", source);
            Assert.Contains("WorkGiver_ConstructDeliverResourcesToFrames", source);
        }
    }
}
