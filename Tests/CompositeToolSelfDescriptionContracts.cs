using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class CompositeToolSelfDescriptionContracts
    {
        private static readonly CompositeToolCallBase[] AllProductionCompositeTools = new CompositeToolCallBase[]
        {
            new PlaceBlueprintCompositeTool(),
            new PlanRoomProjectCompositeTool(),
            new AddWorktableBillCompositeTool(),
            new DesignateResourcesCompositeTool(),
            new ManageZoneCompositeTool(),
            new ManageEquipmentCompositeTool(),
            new StabilizeRestCompositeTool(),
            new EmergencyFleeCompositeTool(),
            new EatAndRecreationCompositeTool(),
            new TriagePatientCompositeTool(),
            new QueryBuildableStructuresCompositeTool()
        };

        [Fact]
        public void All_11_CompositeTools_Declare_RimMindActions_Owner_Valid_Domains_And_Aliases()
        {
            var registry = new ToolRegistry();
            foreach (var tool in AllProductionCompositeTools)
            {
                registry.Register(tool);

                var manifest = tool.Definition.Manifest;
                Assert.NotNull(manifest);
                Assert.Equal("RimMindActions", manifest.OwnerModId);
                Assert.False(string.IsNullOrWhiteSpace(manifest.DisplayName), $"Tool '{tool.Id}' must declare DisplayName");
                Assert.NotEmpty(manifest.Aliases);
                Assert.NotEmpty(manifest.SummaryArgKeys);

                if (tool is QueryBuildableStructuresCompositeTool)
                {
                    Assert.Equal(ToolKind.Query, manifest.Kind);
                    Assert.Null(manifest.DefaultDomain);
                }
                else
                {
                    Assert.Equal(ToolKind.Mutation, manifest.Kind);
                    Assert.NotNull(manifest.DefaultDomain);
                    Assert.Equal(manifest.DefaultDomain.Value.ToString(), manifest.AutonomyDomainId);
                }

                // Canonical ID and every declared alias must resolve back to the tool handler in ToolRegistry
                Assert.Same(tool, registry.FindById(tool.Id));
                foreach (var alias in manifest.Aliases)
                {
                    Assert.Same(tool, registry.FindById(alias));
                }
            }
        }

        [Fact]
        public void SelfTargeted_Metadata_Matches_PawnPersonal_Tools()
        {
            var selfTargetedIds = AllProductionCompositeTools
                .Where(t => t.Manifest.IsSelfTargeted)
                .Select(t => t.Id)
                .OrderBy(id => id)
                .ToList();

            Assert.Contains("actions.stabilize_rest", selfTargetedIds);
            Assert.Contains("actions.eat_and_recreation", selfTargetedIds);
            Assert.Contains("actions.emergency_flee", selfTargetedIds);
            Assert.Contains("actions.triage_patient", selfTargetedIds);
            Assert.Contains("actions_manage_equipment", selfTargetedIds);
            Assert.DoesNotContain("actions_place_blueprint", selfTargetedIds);
            Assert.DoesNotContain("actions_designate_resources", selfTargetedIds);
        }

        [Fact]
        public void PolicyClassifier_Classifies_Survival_Vs_NonSurvival_Arguments_Identically_To_Runtime()
        {
            var placeBlueprint = new PlaceBlueprintCompositeTool();
            var bedPolicy = placeBlueprint.Classify(new ToolCallArgs
            {
                ToolName = placeBlueprint.Id,
                ArgumentsJson = "{\"building_def\":\"Bed\"}"
            });
            var statuePolicy = placeBlueprint.Classify(new ToolCallArgs
            {
                ToolName = placeBlueprint.Id,
                ArgumentsJson = "{\"building_def\":\"SculptureSmall\"}"
            });
            Assert.Equal(AutonomyDomain.EmergencySurvival, bedPolicy.Domain);
            Assert.Equal(RiskLevel.Low, bedPolicy.Risk);
            Assert.Equal(AutonomyDomain.BlueprintPlacement, statuePolicy.Domain);
            Assert.Equal(RiskLevel.Medium, statuePolicy.Risk);

            var addBill = new AddWorktableBillCompositeTool();
            var mealPolicy = addBill.Classify(new ToolCallArgs
            {
                ToolName = addBill.Id,
                ArgumentsJson = "{\"recipe_def\":\"CookMealSimple\"}"
            });
            var sculptureBillPolicy = addBill.Classify(new ToolCallArgs
            {
                ToolName = addBill.Id,
                ArgumentsJson = "{\"recipe_def\":\"Make_SculptureSmall\"}"
            });
            Assert.Equal(AutonomyDomain.EmergencySurvival, mealPolicy.Domain);
            Assert.Equal(RiskLevel.Low, mealPolicy.Risk);
            Assert.Equal(AutonomyDomain.WorktableBills, sculptureBillPolicy.Domain);
            Assert.Equal(RiskLevel.Medium, sculptureBillPolicy.Risk);

            var planRoom = new PlanRoomProjectCompositeTool();
            var shelterPolicy = planRoom.Classify(new ToolCallArgs
            {
                ToolName = planRoom.Id,
                ArgumentsJson = "{\"room_type\":\"Shelter\"}"
            });
            var storagePolicy = planRoom.Classify(new ToolCallArgs
            {
                ToolName = planRoom.Id,
                ArgumentsJson = "{\"room_type\":\"Storage\"}"
            });
            Assert.Equal(AutonomyDomain.EmergencySurvival, shelterPolicy.Domain);
            Assert.Equal(RiskLevel.Low, shelterPolicy.Risk);
            Assert.Equal(AutonomyDomain.BlueprintPlacement, storagePolicy.Domain);
            Assert.Equal(RiskLevel.Medium, storagePolicy.Risk);
        }

        [Fact]
        public void ActionsToolArgumentAdapter_Adapts_CropDesignations_And_RecoveryReason()
        {
            var adapter = new ActionsToolArgumentAdapter();
            Assert.True(adapter.CanAdapt("actions_designate_resources"));
            Assert.True(adapter.CanAdapt("designate_map_order"));
            Assert.True(adapter.CanAdapt("actions.stabilize_rest"));
            Assert.False(adapter.CanAdapt("actions_place_blueprint"));

            var berryArgs = adapter.AdaptArguments(
                "actions_designate_resources",
                JObject.Parse("{\"order_type\":\"CutPlants\",\"target\":\"Plant_Berry\"}"),
                new AgentDecision { ActionIntent = "designate_map_order" },
                1);
            Assert.Equal("Harvest", berryArgs["designation_type"]?.ToString());

            var oakArgs = adapter.AdaptArguments(
                "actions_designate_resources",
                JObject.Parse("{\"order_type\":\"CutPlants\",\"target\":\"Plant_TreeOak\"}"),
                new AgentDecision { ActionIntent = "designate_map_order" },
                1);
            Assert.Equal("ChopWood", oakArgs["designation_type"]?.ToString());

            var restArgs = adapter.AdaptArguments(
                "actions.stabilize_rest",
                new JObject(),
                new AgentDecision { ActionIntent = "stabilize_rest", Reason = "Exhausted after raid" },
                1);
            Assert.Equal("Exhausted after raid", restArgs["reason"]?.ToString());
        }
    }
}
