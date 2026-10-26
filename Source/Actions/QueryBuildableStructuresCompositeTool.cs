using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using RimWorld;
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite query tool allowing AI Agents to inspect available buildable structures,
    /// researched technologies, material costs, stockpile reserves, and urgent infrastructure bottlenecks.
    /// </summary>
    public sealed class QueryBuildableStructuresCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_query_buildable_structures";

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "query",
            Description = "Query buildable structures unlocked by the colony's research, material costs, map stockpile availability, and urgent infrastructure bottlenecks (beds, temperature, recreation).",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\",\"description\":\"Optional category filter: 'furniture', 'temperature', 'production', 'recreation', 'defense', 'livestock', or 'all' (default).\"}},\"required\":[]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            TryGetArgument<string>(args.ArgumentsJson, "category", out var categoryFilter);
            categoryFilter = string.IsNullOrWhiteSpace(categoryFilter) ? "all" : categoryFilter.ToLowerInvariant().Trim();

            // Read-only query: prefer the acting colonist's map, fall back to the viewed map.
            var map = RimMind.Actions.Policies.ActionPermissionGuard.ResolveActorAndMap(args).Map ?? Find.CurrentMap;
            if (map == null)
            {
                var noMapResult = new
                {
                    status = "unavailable",
                    message = "No active map currently spawned."
                };
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
                {
                    ToolCallId = args.ToolCallId,
                    ToolName = Id,
                    Content = JsonConvert.SerializeObject(noMapResult),
                    IsError = false
                }));
            }

            int woodCount = RimMind.Actions.Policies.ResourceSafetyGuard.GetAvailableCount(map, ThingDefOf.WoodLog);
            int steelCount = RimMind.Actions.Policies.ResourceSafetyGuard.GetAvailableCount(map, ThingDefOf.Steel);
            var compDef = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("ComponentsIndustrial");
            int componentCount = compDef != null ? RimMind.Actions.Policies.ResourceSafetyGuard.GetAvailableCount(map, compDef) : 0;

            float outdoorTemp = map.mapTemperature.OutdoorTemp;
            bool hasPower = HasActivePowerGrid(map);

            var bottlenecks = DetectBottlenecks(map, outdoorTemp, hasPower);

            var catalog = GetCuratedStructures();
            var filteredStructures = new List<object>();

            foreach (var s in catalog)
            {
                if (categoryFilter != "all" && !s.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var def = DefDatabase<ThingDef>.GetNamedSilentFail(s.DefName);
                if (def == null) continue;

                // Check research requirements
                if (def.researchPrerequisites != null && def.researchPrerequisites.Any(r => !r.IsFinished))
                {
                    continue;
                }

                bool canAfford = CheckCanAfford(def, s.BaseCostWood, s.BaseCostSteel, s.BaseCostComp, woodCount, steelCount, componentCount);

                filteredStructures.Add(new
                {
                    def_name = s.DefName,
                    label = def.label,
                    category = s.Category,
                    requires_power = s.RequiresPower,
                    cost_summary = s.CostDescription,
                    can_afford = canAfford,
                    purpose = s.Purpose
                });
            }

            var responsePayload = new
            {
                status = "ok",
                colony_status = new
                {
                    outdoor_temperature_c = Math.Round(outdoorTemp, 1),
                    power_grid_active = hasPower,
                    stockpile_materials = new
                    {
                        wood = woodCount,
                        steel = steelCount,
                        components = componentCount
                    },
                    bottlenecks = bottlenecks
                },
                available_structures = filteredStructures
            };

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = JsonConvert.SerializeObject(responsePayload),
                IsError = false
            }));
        }

        private static bool HasActivePowerGrid(Map map)
        {
            if (map.powerNetManager == null) return false;
            foreach (var net in map.powerNetManager.AllNetsListForReading)
            {
                if (net.CurrentEnergyGainRate() > 0f || net.CurrentStoredEnergy() > 0f) return true;
            }
            return false;
        }

        private static List<string> DetectBottlenecks(Map map, float outdoorTemp, bool hasPower)
        {
            var list = new List<string>();
            var colonists = RimMind.Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                .Where(p => RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed)
                .ToList();

            bool sleepingOutside = false;
            bool sleepingOnGround = false;
            foreach (var p in colonists)
            {
                if (p.needs?.mood?.thoughts?.memories == null) continue;
                if (p.needs.mood.thoughts.memories.GetFirstMemoryOfDef(ThoughtDef.Named("SleptOutside")) != null) sleepingOutside = true;
                if (p.needs.mood.thoughts.memories.GetFirstMemoryOfDef(ThoughtDef.Named("SleptOnGround")) != null) sleepingOnGround = true;
            }

            if (sleepingOnGround || sleepingOutside)
            {
                list.Add("⚠️ Colonists sleeping outside or on ground (-4 mood). Build Beds immediately.");
            }

            if (outdoorTemp >= 32f)
            {
                if (!hasPower)
                {
                    list.Add($"⚠️ Warm/Heatwave climate ({outdoorTemp:F1}°C) with NO power. Build PassiveCooler (costs 50 Wood) for indoor cooling.");
                }
                else
                {
                    list.Add($"⚠️ High temperature ({outdoorTemp:F1}°C). Build Cooler for indoor refrigeration/cooling.");
                }
            }
            else if (outdoorTemp <= 5f)
            {
                if (!hasPower)
                {
                    list.Add($"⚠️ Cold/Freezing climate ({outdoorTemp:F1}°C) with NO power. Build Campfire (costs 20 Wood) for heating.");
                }
                else
                {
                    list.Add($"⚠️ Freezing temperature ({outdoorTemp:F1}°C). Build Heater for indoor warming.");
                }
            }

            // Food storage check
            int foodCount = ThingCategoryDefOf.Foods != null ? map.resourceCounter.GetCountIn(ThingCategoryDefOf.Foods) : 0;
            if (foodCount < 10)
            {
                list.Add("⚠️ Low food reserves. Build FueledStove or Campfire to prepare cooked meals.");
            }

            return list;
        }

        private static bool CheckCanAfford(ThingDef def, int wood, int steel, int comp, int woodHave, int steelHave, int compHave)
        {
            if (wood > 0 && woodHave < wood) return false;
            if (steel > 0 && steelHave < steel) return false;
            if (comp > 0 && compHave < comp) return false;
            return true;
        }

        private static List<StructureEntry> GetCuratedStructures()
        {
            return new List<StructureEntry>
            {
                // Furniture
                new StructureEntry("Bed", "furniture", false, "45 WoodLog or Steel", 45, 0, 0, "Comfortable sleeping; prevents SleptOnGround and SleptOutside debuffs"),
                new StructureEntry("Bedroll", "furniture", false, "40 Cloth or Leather", 0, 0, 0, "Portable light sleeping bed"),
                new StructureEntry("TableShort", "furniture", false, "28 WoodLog or Steel", 28, 0, 0, "Small dining/recreation table; prevents AteOnGround debuff"),
                new StructureEntry("Table2x2c", "furniture", false, "40 WoodLog or Steel", 40, 0, 0, "Medium 2x2 dining and recreation table"),
                new StructureEntry("Chair", "furniture", false, "20 WoodLog or Steel", 20, 0, 0, "Comfortable seating at tables and workbenches"),
                new StructureEntry("Stool", "furniture", false, "15 WoodLog or Steel", 15, 0, 0, "Simple fast seating"),

                // Temperature
                new StructureEntry("PassiveCooler", "temperature", false, "50 WoodLog", 50, 0, 0, "Zero-electricity evaporative cooler; cools rooms down to 15°C; perfect for early game heat"),
                new StructureEntry("Campfire", "temperature", false, "20 WoodLog", 20, 0, 0, "Zero-electricity heat source, light, and simple meal cooking"),
                new StructureEntry("Cooler", "temperature", true, "90 Steel + 3 Components", 0, 90, 3, "Powered refrigeration unit; required for food freezers"),
                new StructureEntry("Heater", "temperature", true, "50 Steel + 1 Component", 0, 50, 1, "Powered thermostatically controlled indoor heater"),
                new StructureEntry("Vent", "temperature", false, "30 Steel", 0, 30, 0, "Equalizes temperature between adjacent enclosed rooms"),

                // Production
                new StructureEntry("FueledStove", "production", false, "80 WoodLog + 20 Steel", 80, 20, 0, "Wood-fueled cooking stove to cook fine, simple, or lavish meals"),
                new StructureEntry("TableButcher", "production", false, "75 WoodLog or Steel", 75, 0, 0, "Butchering table to process animal corpses into meat and leather"),
                new StructureEntry("CraftingSpot", "production", false, "Free", 0, 0, 0, "Designated floor spot for simple crafting without material cost"),
                new StructureEntry("TableStonecutter", "production", false, "70 WoodLog or Steel", 70, 0, 0, "Cuts raw stone chunks into building blocks"),
                new StructureEntry("HandTailoringBench", "production", false, "50 WoodLog", 50, 0, 0, "Manual tailoring bench for parkas, dusters, and apparel"),
                new StructureEntry("FueledSmithy", "production", false, "100 Steel", 0, 100, 0, "Fueled forge for melee weapons and armor"),
                new StructureEntry("ResearchBench", "production", false, "50 WoodLog + 20 Steel", 50, 20, 0, "Simple bench for unlocking new colony technologies"),

                // Recreation
                new StructureEntry("HorseshoePin", "recreation", false, "15 WoodLog or Steel", 15, 0, 0, "Outdoor dexterity recreation game"),
                new StructureEntry("ChessTable", "recreation", false, "70 WoodLog", 70, 0, 0, "Indoor cerebral recreation and social gathering spot"),

                // Defense & Security
                new StructureEntry("TrapSpike", "defense", false, "45 WoodLog or Steel", 45, 0, 0, "Deadly spring-loaded deadfall spike trap"),
                new StructureEntry("Sandbags", "defense", false, "5 Steel or Cloth per tile", 0, 5, 0, "Low-cover fortification against enemy ballistic fire"),
                new StructureEntry("Barricade", "defense", false, "5 WoodLog or Metal per tile", 5, 0, 0, "Quick defensive cover built from wood or stone"),
                new StructureEntry("Wall", "defense", false, "5 WoodLog or Stone per tile", 5, 0, 0, "Encloses rooms, provides roof support, and blocks enemies"),
                new StructureEntry("Door", "defense", false, "25 WoodLog or Steel", 25, 0, 0, "Provides passage through walls while maintaining room insulation"),

                // Livestock
                new StructureEntry("PenMarker", "livestock", false, "20 WoodLog", 20, 0, 0, "Marks an enclosed pasture/pen for domestic herd animals"),
                new StructureEntry("Fence", "livestock", false, "2 WoodLog per tile", 2, 0, 0, "Low wooden barrier that encloses pen animals"),
                new StructureEntry("FenceGate", "livestock", false, "10 WoodLog", 10, 0, 0, "Passage gate for colonists into animal pens")
            };
        }

        private class StructureEntry
        {
            public string DefName { get; }
            public string Category { get; }
            public bool RequiresPower { get; }
            public string CostDescription { get; }
            public int BaseCostWood { get; }
            public int BaseCostSteel { get; }
            public int BaseCostComp { get; }
            public string Purpose { get; }

            public StructureEntry(
                string defName,
                string category,
                bool requiresPower,
                string costDescription,
                int baseCostWood,
                int baseCostSteel,
                int baseCostComp,
                string purpose)
            {
                DefName = defName;
                Category = category;
                RequiresPower = requiresPower;
                CostDescription = costDescription;
                BaseCostWood = baseCostWood;
                BaseCostSteel = baseCostSteel;
                BaseCostComp = baseCostComp;
                Purpose = purpose;
            }
        }
    }
}
