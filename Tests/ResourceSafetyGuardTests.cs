using RimMind.Actions.Policies;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class ResourceSafetyGuardTests
    {
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

        [Theory]
        [InlineData(79, 500, 50, 0, false)]   // Fixed-cost WoodLog building (e.g. PassiveCooler) below MinWoodReserve
        [InlineData(80, 500, 50, 0, true)]    // Meets MinWoodReserve (80 >= max(80, 50))
        [InlineData(100, 500, 120, 0, false)] // Multi-blueprint room project exceeds available wood even above reserve
        [InlineData(120, 500, 120, 0, true)]  // Enough wood for multi-blueprint room project
        [InlineData(500, 49, 0, 20, false)]   // Fixed-cost Steel building below MinSteelReserve
        [InlineData(500, 80, 0, 90, false)]   // High-cost Steel building (e.g. Cooler) exceeds available steel
        [InlineData(500, 90, 0, 90, true)]    // Enough steel for fixed-cost building
        [InlineData(10, 10, 0, 0, true)]      // Zero wood/steel requirements (e.g. stone-only structure)
        public void EvaluateMaterialRequirements_Enforces_FixedCost_And_Project_Thresholds(
            int availableWood,
            int availableSteel,
            int requiredWood,
            int requiredSteel,
            bool shouldPass)
        {
            var result = ResourceSafetyPolicy.EvaluateMaterialRequirements(
                availableWood, availableSteel, requiredWood, requiredSteel);

            if (shouldPass)
            {
                Assert.True(result.IsOk);
            }
            else
            {
                Assert.False(result.IsOk);
                Assert.Contains("[Safety Guard]", result.Error.Message);
            }
        }
    }
}
