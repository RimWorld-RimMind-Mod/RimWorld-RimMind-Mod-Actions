using System;
using System.Collections.Generic;
using System.IO;
using RimMind.Actions.Policies;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class QueryBuildableStructuresCompositeToolTests
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
        public void QueryBuildableStructures_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("public sealed class QueryBuildableStructuresCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_query_buildable_structures\";", source);
        }

        [Fact]
        public void QueryBuildableStructures_Declares_Category_Filter_Schema()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("\"category\"", source);
            Assert.Contains("furniture", source);
            Assert.Contains("temperature", source);
            Assert.Contains("production", source);
            Assert.Contains("recreation", source);
            Assert.Contains("defense", source);
            Assert.Contains("livestock", source);
        }

        [Fact]
        public void QueryBuildableStructures_Contains_Bottleneck_Detection_For_Beds_And_Climate()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("DetectBottlenecks", source);
            Assert.Contains("SleptOutside", source);
            Assert.Contains("SleptOnGround", source);
            Assert.Contains("PassiveCooler", source);
            Assert.Contains("Campfire", source);
        }

        [Fact]
        public void QueryBuildableStructures_Uses_Dynamic_DefDatabase_Discovery_Instead_Of_Hardcoded_List()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("DefDatabase<ThingDef>.AllDefsListForReading", source);
            Assert.Contains("BuildableByPlayer", source);
            Assert.Contains("IsResearchFinished", source);
            Assert.Contains("BuildableStructureCatalogPolicy.FilterAndProject", source);
            Assert.DoesNotContain("GetCuratedStructures", source);
            Assert.DoesNotContain("\"HorseshoePin\"", source);
            Assert.DoesNotContain("\"ResearchBench\"", source);
        }

        [Fact]
        public void BuildableStructureCatalogPolicy_Classifies_Vanilla_And_Modded_Buildings_Accurately()
        {
            Assert.Equal("production", BuildableStructureCatalogPolicy.ClassifyCategory("Production", "SimpleResearchBench", isWorkTable: true));
            Assert.Equal("recreation", BuildableStructureCatalogPolicy.ClassifyCategory("Joy", "HorseshoesPin", isJoy: true));
            Assert.Equal("furniture", BuildableStructureCatalogPolicy.ClassifyCategory("Furniture", "Bed", isBed: true));
            Assert.Equal("temperature", BuildableStructureCatalogPolicy.ClassifyCategory("Temperature", "PassiveCooler", isTempControl: true));
            Assert.Equal("livestock", BuildableStructureCatalogPolicy.ClassifyCategory("Structure", "PenMarker", isFenceOrPen: true));
            Assert.Equal("production", BuildableStructureCatalogPolicy.ClassifyCategory("Production", "Modded_AutoSmelter", isWorkTable: true));
            Assert.Equal("power", BuildableStructureCatalogPolicy.ClassifyCategory("Power", "Modded_SolarTurbine", generatesPower: true));
        }

        [Fact]
        public void BuildableStructureCatalogPolicy_Filters_FormatsCosts_And_EvaluatesAffordability()
        {
            var stock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["WoodLog"] = 120,
                ["Steel"] = 30,
                ["ComponentIndustrial"] = 1
            };

            var descriptors = new[]
            {
                new BuildableStructureDescriptor
                {
                    DefName = "SimpleResearchBench",
                    Label = "simple research bench",
                    Category = "production",
                    CostStuffCount = 75,
                    BestStuffLabel = "WoodLog",
                    BestStuffAvailable = 120,
                    FixedCosts = new[] { new BuildableMaterialCost("Steel", "Steel", 25) },
                    Purpose = "A simple bench with writing implements and low-tech scientific tools."
                },
                new BuildableStructureDescriptor
                {
                    DefName = "HorseshoesPin",
                    Label = "horseshoes pin",
                    Category = "recreation",
                    CostStuffCount = 10,
                    BestStuffLabel = "WoodLog",
                    BestStuffAvailable = 120,
                    Purpose = "A simple ancient game played with a few horseshoes and a large pin stuck in the ground."
                },
                new BuildableStructureDescriptor
                {
                    DefName = "Modded_ArcFurnace",
                    Label = "arc furnace",
                    Category = "production",
                    RequiresPower = true,
                    FixedCosts = new[]
                    {
                        new BuildableMaterialCost("Steel", "Steel", 150),
                        new BuildableMaterialCost("ComponentIndustrial", "ComponentIndustrial", 4)
                    },
                    Purpose = "Modded high-speed electric smelter."
                }
            };

            var production = BuildableStructureCatalogPolicy.FilterAndProject(
                descriptors,
                "production",
                def => stock.TryGetValue(def, out int c) ? c : 0);

            Assert.Equal(2, production.Count);
            // Affordable structure ranks first
            Assert.Equal("SimpleResearchBench", production[0].DefName);
            Assert.True(production[0].CanAfford);
            Assert.Equal("75 WoodLog + 25 Steel", production[0].CostSummary);

            Assert.Equal("Modded_ArcFurnace", production[1].DefName);
            Assert.False(production[1].CanAfford);
            Assert.True(production[1].RequiresPower);
            Assert.Equal("150 Steel + 4 ComponentIndustrial", production[1].CostSummary);

            var rec = BuildableStructureCatalogPolicy.FilterAndProject(
                descriptors,
                "recreation",
                def => stock.TryGetValue(def, out int c) ? c : 0);
            Assert.Single(rec);
            Assert.Equal("HorseshoesPin", rec[0].DefName);
            Assert.True(rec[0].CanAfford);
        }
    }
}
