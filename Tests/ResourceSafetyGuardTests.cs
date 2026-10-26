using System;
using System.IO;
using RimMind.Actions.Policies;
using RimMind.Domain.Agent.Policies;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class ResourceSafetyGuardTests
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
        public void ResourceSafetyPolicy_Constants_Match_RFC_Specifications()
        {
            Assert.Equal(80, ResourceSafetyPolicy.MinWoodReserve);
            Assert.Equal(50, ResourceSafetyPolicy.MinSteelReserve);
            Assert.Equal(2, ResourceSafetyPolicy.MaxConcurrentBlueprints);
        }

        [Theory]
        [InlineData(79, "WoodLog", false)]
        [InlineData(80, "WoodLog", true)]
        [InlineData(150, "WoodLog", true)]
        [InlineData(49, "Steel", false)]
        [InlineData(50, "Steel", true)]
        [InlineData(100, "Steel", true)]
        [InlineData(0, "Plasteel", true)]
        public void EvaluateReserves_Enforces_Hard_Baselines(int availableAmount, string stuff, bool shouldPass)
        {
            int wood = stuff == "WoodLog" ? availableAmount : 500;
            int steel = stuff == "Steel" ? availableAmount : 500;

            var result = ResourceSafetyPolicy.EvaluateReserves(wood, steel, stuff);

            if (shouldPass)
            {
                Assert.True(result.IsOk, $"Expected OK for {stuff} with amount {availableAmount}");
            }
            else
            {
                Assert.False(result.IsOk, $"Expected Error for {stuff} with amount {availableAmount}");
                Assert.Contains("[Safety Guard]", result.Error.Message);
            }
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        public void EvaluateBlueprintCapacity_Enforces_Max_Two_Concurrent_Blueprints(int activeBlueprints, bool shouldPass)
        {
            var result = ResourceSafetyPolicy.EvaluateBlueprintCapacity(activeBlueprints);

            if (shouldPass)
            {
                Assert.True(result.IsOk);
            }
            else
            {
                Assert.False(result.IsOk);
                Assert.Contains("[Safety Guard]", result.Error.Message);
                Assert.Contains("concurrency limit (2)", result.Error.Message);
            }
        }

        [Fact]
        public void PlaceBlueprintCompositeTool_Integrates_Permission_And_Safety_Guards()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "PlaceBlueprintCompositeTool.cs"));

            Assert.Contains("ActionPermissionGuard.CheckPermission", source);
            Assert.Contains("AutonomyDomain.BlueprintPlacement", source);
            Assert.Contains("ResourceSafetyGuard.CheckConstructionSafety", source);
        }

        [Fact]
        public void ActionPermissionGuard_Source_Enforces_TriState_Branches()
        {
            var source = ReadActionsSource(Path.Combine("Policies", "ActionPermissionGuard.cs"));

            Assert.Contains("AutonomyLevel.Disabled", source);
            Assert.Contains("AutonomyLevel.RequireApproval", source);
            Assert.Contains("AutonomyLevel.FullAutonomous", source);
        }
    }
}
