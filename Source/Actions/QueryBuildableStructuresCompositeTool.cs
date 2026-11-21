using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimMind.Actions.Spatial;
using RimMind.Presentation.Api;
using RimWorld;
#endif
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite query tool allowing AI Agents to dynamically inspect player-buildable structures
    /// discovered from <c>DefDatabase&lt;ThingDef&gt;</c> (covering both vanilla and modded buildings),
    /// material costs, stockpile reserves, spatial layout, and urgent infrastructure bottlenecks.
    /// </summary>
    public sealed class QueryBuildableStructuresCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_query_buildable_structures";

        private static readonly string[] Aliases = new[]
        {
            "query_buildable_structures",
            "actions.query_buildable_structures"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "category"
        };

        protected override ToolKind ToolKind => ToolKind.Query;
        protected override AutonomyDomain? DefaultAutonomyDomain => null;
        protected override ToolScope? ToolScopes => ToolScope.ColonyBuild | ToolScope.ColonyManage;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => false;
        protected override string? DisplayName => "Query Buildable Structures";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "query",
            Description = "Dynamically queries player-buildable structures unlocked by colony research (vanilla and modded), material costs, stockpile availability, spatial layout, and urgent infrastructure bottlenecks.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\",\"description\":\"Optional category filter: 'furniture', 'temperature', 'production', 'recreation', 'defense', 'livestock', 'power', 'structure', or 'all' (default).\"}},\"required\":[]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            var argsRoot = TryParseArgumentsObject(args.ArgumentsJson);
            TryGetArgument<string>(argsRoot, "category", out var categoryFilter);
            categoryFilter = string.IsNullOrWhiteSpace(categoryFilter) ? "all" : categoryFilter.ToLowerInvariant().Trim();

#if RIMMIND_ACTIONS_TESTS
            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = JsonConvert.SerializeObject(new { status = "ok", category = categoryFilter }),
                IsError = false
            }));
#else
            // Read-only query: prefer the acting colonist's map, fall back to the viewed map.
            var actorResolution = ActionPermissionGuard.ResolveActorAndMap(args, parsedArgs: argsRoot);
            var map = actorResolution.Map ?? Find.CurrentMap;
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

            var anchorPawn = actorResolution.Pawn;
            IntVec3 fallbackAnchor = anchorPawn != null && anchorPawn.Spawned && anchorPawn.Map == map ? anchorPawn.Position : map.Center;
            ColonySpatialSnapshot spatialSnapshot = MapSpatialSnapshotBuilder.Build(map, fallbackAnchor.x, fallbackAnchor.z, radius: 22);
            var roomSummaries = spatialSnapshot.Rooms
                .Take(10)
                .Select(r => new
                {
                    room_id = r.RoomId,
                    role = r.Role,
                    center_x = r.CenterX,
                    center_z = r.CenterZ,
                    cells = r.CellCount,
                    beds = r.BedCount,
                    worktables = r.WorktableCount,
                    tables = r.TableCount
                })
                .ToList();
            string spatialAsciiMap = SpatialAsciiRenderer.RenderCompactAsciiGrid(
                spatialSnapshot,
                spatialSnapshot.ColonyAnchorX,
                spatialSnapshot.ColonyAnchorZ,
                radius: 10);

            int woodCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
            int steelCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);
            var compDef = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("ComponentsIndustrial");
            int componentCount = compDef != null ? MapResourceCounter.GetAvailableCount(map, compDef) : 0;

            float outdoorTemp = map.mapTemperature.OutdoorTemp;
            bool hasPower = HasActivePowerGrid(map);

            var bottlenecks = DetectBottlenecks(map, outdoorTemp, hasPower);

            var availableByDefName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int LookupAvailableCount(string defName)
            {
                if (string.IsNullOrWhiteSpace(defName)) return 0;
                if (availableByDefName.TryGetValue(defName, out int cached)) return cached;
                var resolved = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                int count = resolved != null ? MapResourceCounter.GetAvailableCount(map, resolved) : 0;
                availableByDefName[defName] = count;
                return count;
            }

            var dynamicDescriptors = DiscoverBuildableStructures(map, LookupAvailableCount);
            var catalogItems = BuildableStructureCatalogPolicy.FilterAndProject(
                dynamicDescriptors,
                categoryFilter,
                LookupAvailableCount,
                BuildableStructureCatalogPolicy.DefaultMaxResults);

            var filteredStructures = catalogItems
                .Select(s => new
                {
                    def_name = s.DefName,
                    label = s.Label,
                    category = s.Category,
                    requires_power = s.RequiresPower,
                    cost_summary = s.CostSummary,
                    can_afford = s.CanAfford,
                    purpose = s.Purpose
                })
                .ToList();

            var responsePayload = new
            {
                status = "ok",
                colony_anchor = new
                {
                    x = spatialSnapshot.ColonyAnchorX,
                    z = spatialSnapshot.ColonyAnchorZ,
                    has_base = spatialSnapshot.HasBaseAnchor
                },
                rooms = roomSummaries,
                spatial_ascii_map = spatialAsciiMap,
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
#endif
        }

#if !RIMMIND_ACTIONS_TESTS
        private static List<BuildableStructureDescriptor> DiscoverBuildableStructures(
            Map map,
            Func<string, int> lookupAvailableCount)
        {
            var results = new List<BuildableStructureDescriptor>();
            var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;

            for (int i = 0; i < allDefs.Count; i++)
            {
                var def = allDefs[i];
                if (def == null
                    || def.category != ThingCategory.Building
                    || def.IsFrame
                    || def.IsBlueprint
                    || !def.BuildableByPlayer
                    || !def.IsResearchFinished
                    || def.designationCategory == null)
                {
                    continue;
                }

                var powerComp = def.GetCompProperties<CompProperties_Power>();
                bool requiresPower = powerComp != null && powerComp.PowerConsumption > 0f;
                bool generatesPower = powerComp != null && powerComp.PowerConsumption < 0f;

                bool isBed = def.thingClass != null && typeof(Building_Bed).IsAssignableFrom(def.thingClass);
                bool isWorkTable = def.thingClass != null && typeof(Building_WorkTable).IsAssignableFrom(def.thingClass);
                bool isTable = def.surfaceType == SurfaceType.Eat;
                bool isJoy = def.building?.joyKind != null;
                bool isDefense = (def.building != null && def.building.IsTurret)
                    || (def.thingClass != null && typeof(Building_Trap).IsAssignableFrom(def.thingClass))
                    || string.Equals(def.designationCategory.defName, "Security", StringComparison.OrdinalIgnoreCase);
                bool isFenceOrPen = (def.building != null && def.building.isFence)
                    || def.GetCompProperties<CompProperties_AnimalPenMarker>() != null
                    || (def.defName != null && (def.defName.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) >= 0
                        || def.defName.IndexOf("PenMarker", StringComparison.OrdinalIgnoreCase) >= 0));
                bool isTempControl = def.GetCompProperties<CompProperties_TempControl>() != null
                    || def.GetCompProperties<CompProperties_HeatPusher>() != null;

                string category = BuildableStructureCatalogPolicy.ClassifyCategory(
                    def.designationCategory.defName,
                    def.defName,
                    isBed: isBed,
                    isWorkTable: isWorkTable,
                    isTable: isTable,
                    isJoy: isJoy,
                    isDefense: isDefense,
                    isFenceOrPen: isFenceOrPen,
                    isTempControl: isTempControl,
                    generatesPower: generatesPower);

                int stuffCount = def.MadeFromStuff ? def.costStuffCount : 0;
                string? bestStuffLabel = null;
                int bestStuffAvailable = 0;
                if (stuffCount > 0)
                {
                    ThingDef? bestStuff = null;
                    foreach (var allowed in GenStuff.AllowedStuffsFor(def))
                    {
                        if (allowed == null) continue;
                        int have = lookupAvailableCount(allowed.defName);
                        if (bestStuff == null || have > bestStuffAvailable)
                        {
                            bestStuff = allowed;
                            bestStuffAvailable = have;
                        }
                    }

                    if (bestStuff == null)
                    {
                        bestStuff = GenStuff.DefaultStuffFor(def);
                        bestStuffAvailable = bestStuff != null ? lookupAvailableCount(bestStuff.defName) : 0;
                    }

                    bestStuffLabel = bestStuff?.defName ?? bestStuff?.label ?? "Stuff";
                }

                var fixedCosts = new List<BuildableMaterialCost>();
                if (def.costList != null)
                {
                    for (int c = 0; c < def.costList.Count; c++)
                    {
                        var item = def.costList[c];
                        if (item?.thingDef != null && item.count > 0)
                        {
                            fixedCosts.Add(new BuildableMaterialCost(
                                item.thingDef.defName,
                                item.thingDef.defName,
                                item.count));
                        }
                    }
                }

                int priorityWeight = ComputePriorityWeight(category, isBed, isWorkTable, isTempControl, isJoy, requiresPower);

                results.Add(new BuildableStructureDescriptor
                {
                    DefName = def.defName ?? string.Empty,
                    Label = def.label ?? def.defName ?? string.Empty,
                    Category = category,
                    RequiresPower = requiresPower,
                    CostStuffCount = stuffCount,
                    BestStuffLabel = bestStuffLabel,
                    BestStuffAvailable = bestStuffAvailable,
                    FixedCosts = fixedCosts,
                    Purpose = def.description ?? string.Empty,
                    PriorityWeight = priorityWeight
                });
            }

            return results;
        }

        private static int ComputePriorityWeight(
            string category,
            bool isBed,
            bool isWorkTable,
            bool isTempControl,
            bool isJoy,
            bool requiresPower)
        {
            int weight = 0;
            if (isBed) weight += 30;
            if (isWorkTable) weight += 25;
            if (isTempControl) weight += 25;
            if (isJoy) weight += 20;
            if (!requiresPower) weight += 10;
            if (category == "furniture" || category == "production" || category == "temperature")
                weight += 10;
            return weight;
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
            var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                .Where(p => RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed)
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
#endif
    }
}
