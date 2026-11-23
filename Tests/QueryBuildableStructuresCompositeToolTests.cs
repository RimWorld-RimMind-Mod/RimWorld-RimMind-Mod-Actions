using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Actions.Policies;
using RimMind.Actions.Spatial;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class QueryBuildableStructuresCompositeToolTests
    {
        [Fact]
        public void QueryBuildableStructures_Declares_QueryMetadata_And_CategorySchema()
        {
            var tool = new QueryBuildableStructuresCompositeTool();
            Assert.Equal("actions_query_buildable_structures", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);
            Assert.Equal(ToolKind.Query, tool.Manifest.Kind);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            string desc = schema["properties"]!["category"]!["description"]!.ToString();
            Assert.Contains("furniture", desc);
            Assert.Contains("temperature", desc);
            Assert.Contains("production", desc);
            Assert.Contains("recreation", desc);
            Assert.Contains("defense", desc);
            Assert.Contains("livestock", desc);
            Assert.Contains("power", desc);
            Assert.Contains("structure", desc);
        }

        [Fact]
        public async Task QueryBuildableStructures_Normalizes_Category_And_Guards_MainThread()
        {
            var tool = new QueryBuildableStructuresCompositeTool();
            try
            {
                var defaultRes = await tool.ExecuteAsync(
                    new ToolCallArgs { ToolCallId = "q-1", ToolName = tool.Id, ArgumentsJson = "{}" },
                    CancellationToken.None);
                Assert.True(defaultRes.IsOk);
                Assert.Equal("all", JObject.Parse(defaultRes.Value.Content)["category"]?.ToString());

                var filteredRes = await tool.ExecuteAsync(
                    new ToolCallArgs { ToolCallId = "q-2", ToolName = tool.Id, ArgumentsJson = "{\"category\":\"  Furniture \"}" },
                    CancellationToken.None);
                Assert.True(filteredRes.IsOk);
                Assert.Equal("furniture", JObject.Parse(filteredRes.Value.Content)["category"]?.ToString());

                Verse.UnityData.IsInMainThread = false;
                var offThread = await tool.ExecuteAsync(
                    new ToolCallArgs { ToolCallId = "q-3", ToolName = tool.Id, ArgumentsJson = "{}" },
                    CancellationToken.None);
                Assert.True(offThread.IsErr);
                Assert.Equal(RimMindErrorCode.ToolExecutionFailed, offThread.Error.Code);
            }
            finally
            {
                Verse.UnityData.IsInMainThread = true;
            }
        }

        [Fact]
        public void SpatialBuildingClassifier_Delegates_Consistently_To_BuildableStructureCatalogPolicy()
        {
            Assert.Equal(SpatialBuildingCategory.Sleeping, SpatialBuildingClassifier.Classify("Bed", isBed: true));
            Assert.Equal(SpatialBuildingCategory.Production, SpatialBuildingClassifier.Classify("Modded_AutoSmelter", isWorkTable: true));
            Assert.Equal(SpatialBuildingCategory.DiningRecreation, SpatialBuildingClassifier.Classify("Table2x2c", isTable: true));
            Assert.Equal(SpatialBuildingCategory.DiningRecreation, SpatialBuildingClassifier.Classify("HorseshoesPin", isJoy: true));
            Assert.Equal(SpatialBuildingCategory.TemperatureLight, SpatialBuildingClassifier.Classify("PassiveCooler"));
            Assert.Equal(SpatialBuildingCategory.Defense, SpatialBuildingClassifier.Classify("Turret_MiniTurret", isDefense: true));
            Assert.Equal(SpatialBuildingCategory.Structure, SpatialBuildingClassifier.Classify("Wall"));
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
