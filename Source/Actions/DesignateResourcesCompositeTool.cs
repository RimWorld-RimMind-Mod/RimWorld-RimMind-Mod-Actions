using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;
using RimWorld;
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool that autonomously designates map resources for colonist gathering and harvesting
    /// (e.g. wild berries, healroot, mature timber trees, surface mineral veins)
    /// respecting colonist worktab queues, leader permissions, and distance limits.
    /// </summary>
    public sealed class DesignateResourcesCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_designate_resources";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Autonomously designates resources on the map for colonist worktab execution (e.g. harvesting ripe wild berries/healroot, chopping mature trees for wood, or mining exposed mineral veins).",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"designation_type\":{\"type\":\"string\",\"description\":\"Type of designation: 'Harvest' (ripe wild food/plants), 'ChopWood' (mature timber trees), or 'Mine' (mineral ores).\"},\"radius\":{\"type\":\"integer\",\"description\":\"Search radius in tiles from anchor colonist/base center (default: 25, max: 60).\"},\"target_name\":{\"type\":\"string\",\"description\":\"Optional filter for specific defName or label (e.g. 'Plant_Berry', 'Plant_TreeOak', 'MineableSteel').\"},\"max_count\":{\"type\":\"integer\",\"description\":\"Maximum number of items to designate in one call (default: 15, max: 50).\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to center search and check autonomy permissions.\"}},\"required\":[\"designation_type\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            if (!TryGetArgument<string>(args.ArgumentsJson, "designation_type", out var designationTypeStr)
                || string.IsNullOrWhiteSpace(designationTypeStr))
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, "Missing required parameter 'designation_type' (Harvest, ChopWood, or Mine).")));
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for resource designation.")));
            }

            // 1. Resolve Colonist Pawn and Permission Check
            Pawn? pawn = ActionPermissionGuard.ResolveColonistOnMap(map, args, "pawn_id", fallbackToAnyActiveColonist: true);

            var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, AutonomyDomain.ResourceMarking, args);
            if (!permCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
            }

            // 2. Resolve Center Cell
            IntVec3 centerCell = pawn?.Position ?? map.Center;

            // 3. Resolve Radius and Count Limit
            TryGetArgument<int>(args.ArgumentsJson, "radius", out var radius);
            if (radius <= 0) radius = 25;
            radius = Math.Clamp(radius, 5, 60);

            TryGetArgument<int>(args.ArgumentsJson, "max_count", out var maxCount);
            if (maxCount <= 0) maxCount = 15;
            maxCount = Math.Clamp(maxCount, 1, 50);

            TryGetArgument<string>(args.ArgumentsJson, "target_name", out var targetNameFilter);
            if (string.IsNullOrWhiteSpace(targetNameFilter))
            {
                TryGetArgument<string>(args.ArgumentsJson, "target", out targetNameFilter);
            }
            targetNameFilter = targetNameFilter?.Trim();
            string rawTarget = targetNameFilter ?? "";
            bool targetImpliesTrees =
                rawTarget.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawTarget.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawTarget.IndexOf("树", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawTarget.IndexOf("木", StringComparison.OrdinalIgnoreCase) >= 0;

            if (IsGenericResourceFilter(targetNameFilter))
            {
                targetNameFilter = null;
            }

            // 4. Resolve DesignationDef and Filter
            string mode = designationTypeStr.Trim().ToLowerInvariant();
            DesignationDef desDef;
            Func<Thing, bool> candidatePredicate;

            if (mode.Contains("harvest") && !targetImpliesTrees)
            {
                desDef = DesignationDefOf.HarvestPlant;
                candidatePredicate = t =>
                {
                    if (t is not Plant plant) return false;
                    if (plant.def.plant == null || plant.def.plant.IsTree || !plant.def.plant.Harvestable) return false;
                    if (!plant.HarvestableNow && plant.Growth < 0.7f) return false;
                    if (map.designationManager.DesignationOn(plant, desDef) != null) return false;
                    if (!string.IsNullOrWhiteSpace(targetNameFilter))
                    {
                        if (!plant.def.defName.Contains(targetNameFilter, StringComparison.OrdinalIgnoreCase)
                            && !plant.Label.Contains(targetNameFilter, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                };
            }
            else if (mode.Contains("chop") || mode.Contains("wood") || mode.Contains("tree") || targetImpliesTrees)
            {
                // In RimWorld, mature trees for wood harvest are designated with HarvestPlant
                desDef = DesignationDefOf.HarvestPlant;
                candidatePredicate = t =>
                {
                    if (t is not Plant plant) return false;
                    if (plant.def.plant == null || !plant.def.plant.IsTree) return false;
                    if (!plant.HarvestableNow) return false;
                    if (map.designationManager.DesignationOn(plant, desDef) != null) return false;
                    if (!string.IsNullOrWhiteSpace(targetNameFilter))
                    {
                        if (!plant.def.defName.Contains(targetNameFilter, StringComparison.OrdinalIgnoreCase)
                            && !plant.Label.Contains(targetNameFilter, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                };
            }
            else if (mode.Contains("cut") || mode.Contains("clear"))
            {
                desDef = DesignationDefOf.CutPlant;
                candidatePredicate = t =>
                {
                    if (t is not Plant plant) return false;
                    if (map.designationManager.DesignationOn(plant, desDef) != null) return false;
                    return true;
                };
            }
            else if (mode.Contains("mine") || mode.Contains("ore") || mode.Contains("mineral"))
            {
                desDef = DesignationDefOf.Mine;
                candidatePredicate = t =>
                {
                    if (t.def.mineable != true && t is not Mineable) return false;
                    if (map.designationManager.DesignationOn(t, desDef) != null) return false;
                    if (!string.IsNullOrWhiteSpace(targetNameFilter))
                    {
                        if (!t.def.defName.Contains(targetNameFilter, StringComparison.OrdinalIgnoreCase)
                            && !t.Label.Contains(targetNameFilter, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                };
            }
            else
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, $"Unknown designation type '{designationTypeStr}'. Supported: 'Harvest', 'ChopWood', 'Mine'.")));
            }

            // 5. Query Candidates within Radius (with automatic expansion up to map-wide 120 tiles if none within initial radius)
            var candidates = new List<Thing>();
            var allSpawnedThings = map.listerThings?.AllThings ?? new List<Thing>();

            foreach (var thing in allSpawnedThings)
            {
                if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                if (thing.Position.Fogged(map)) continue;
                if (!thing.Position.InHorDistOf(centerCell, radius)) continue;

                if (candidatePredicate(thing))
                {
                    candidates.Add(thing);
                }
            }

            if (candidates.Count == 0 && radius < 120)
            {
                foreach (var thing in allSpawnedThings)
                {
                    if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                    if (thing.Position.Fogged(map)) continue;
                    if (!thing.Position.InHorDistOf(centerCell, 120)) continue;

                    if (candidatePredicate(thing))
                    {
                        candidates.Add(thing);
                    }
                }
            }

            // Fallback: if LLM provided a descriptive target_name filter (e.g. "berries_and_edible_wild_plants" or "成熟作物")
            // that matched 0 exact ThingDefs, clear the filter and scan for any valid target of the requested designation type.
            if (candidates.Count == 0 && targetNameFilter != null)
            {
                targetNameFilter = null;
                foreach (var thing in allSpawnedThings)
                {
                    if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                    if (thing.Position.Fogged(map)) continue;
                    if (!thing.Position.InHorDistOf(centerCell, Math.Max(radius, 120))) continue;

                    if (candidatePredicate(thing))
                    {
                        candidates.Add(thing);
                    }
                }
            }

            // Seasonal fallback: in winter when 0 edible wild plants/crops exist on the map for 'harvest',
            // check if mature trees can be harvested for essential survival wood/fuel.
            if (candidates.Count == 0 && mode.Contains("harvest"))
            {
                foreach (var thing in allSpawnedThings)
                {
                    if (thing is not Plant tree || !tree.Spawned || tree.Destroyed) continue;
                    if (tree.Position.Fogged(map)) continue;
                    if (tree.def?.plant == null || !tree.def.plant.IsTree) continue;
                    if (!tree.HarvestableNow) continue;
                    if (map.designationManager.DesignationOn(tree, DesignationDefOf.HarvestPlant) != null) continue;
                    candidates.Add(tree);
                }
            }

            // Also unforbid any already-dropped WoodLog or raw food on the map so colonists can haul them
            int unforbidCount = 0;
            foreach (var thing in allSpawnedThings)
            {
                if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                if (thing.Position.Fogged(map)) continue;
                if (thing.def == ThingDefOf.WoodLog || (thing.def?.IsNutritionGivingIngestible == true && !thing.def.IsCorpse))
                {
                    if (thing.IsForbidden(Faction.OfPlayer))
                    {
                        thing.SetForbidden(false, false);
                        unforbidCount++;
                    }
                }
            }

            if (candidates.Count == 0)
            {
                int existingDesignations = map.designationManager?.AllDesignations?.Count(d => d.def == desDef) ?? 0;
                if (unforbidCount > 0 || existingDesignations > 0)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                        ToolResult.Ok(
                            $"No new undesignated '{designationTypeStr}' targets found near ({centerCell.x}, {centerCell.z}) (existing active designations: {existingDesignations}, unforbidden resource stacks: {unforbidCount}).",
                            args.ToolCallId,
                            Id)));
                }
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(
                        Id,
                        $"No undesignated '{designationTypeStr}' targets found within {radius} tiles of ({centerCell.x}, {centerCell.z}).")));
            }

            // 6. Sort by Distance and Apply Designation
            var selected = candidates
                .OrderBy(t => t.Position.DistanceToSquared(centerCell))
                .Take(maxCount)
                .ToList();

            int addedCount = 0;
            foreach (var target in selected)
            {
                map.designationManager.AddDesignation(new Designation(target, desDef));
                addedCount++;
            }

            string targetSample = string.Join(", ", selected.Take(3).Select(t => t.LabelShort));
            if (selected.Count > 3) targetSample += $" (+{addedCount - 3} more)";

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok(
                    $"Successfully designated {addedCount} '{desDef.label}' target(s) ({targetSample}). Colonists will execute via work priorities.",
                    args.ToolCallId,
                    Id)));
        }

        private static bool IsGenericResourceFilter(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            var f = filter!.Trim();
            if (DefDatabase<ThingDef>.GetNamedSilentFail(f) != null) return false;
            return f.Equals("Nearby", StringComparison.OrdinalIgnoreCase)
                || f.Equals("All", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Any", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Trees", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Tree", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Wood", StringComparison.OrdinalIgnoreCase)
                || f.Equals("WoodLog", StringComparison.OrdinalIgnoreCase)
                || f.Equals("MatureTrees", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Plants", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Plant", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Crops", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Crop", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Wild", StringComparison.OrdinalIgnoreCase)
                || f.Equals("WildPlants", StringComparison.OrdinalIgnoreCase)
                || f.Equals("WildBerries", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Berries", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Food", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Edible", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Ore", StringComparison.OrdinalIgnoreCase)
                || f.Equals("Minerals", StringComparison.OrdinalIgnoreCase)
                || f.Contains(" ")
                || f.Contains("_")
                || f.Any(c => c > 127);
        }
    }
}
