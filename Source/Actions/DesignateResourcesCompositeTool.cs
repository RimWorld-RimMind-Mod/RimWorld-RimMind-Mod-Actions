using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimWorld;
#endif
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

        private static readonly string[] Aliases = new[]
        {
            "designate_resources",
            "designate_map_order",
            "actions.designate_resources"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "designation_type",
            "target_name"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.ResourceMarking;
        protected override ToolScope? ToolScopes => ToolScope.ColonyManage;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => false;
        protected override string? DisplayName => "Designate Resources";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Autonomously designates resources on the map for colonist worktab execution (e.g. harvesting ripe wild berries/healroot, chopping mature trees for wood, or mining exposed mineral veins).",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"designation_type\":{\"type\":\"string\",\"description\":\"Type of designation: 'Harvest' (ripe wild food/plants), 'ChopWood' (mature timber trees), 'Mine' (mineral ores), or 'Hunt' (safe wild animals for meat when food is low).\"},\"radius\":{\"type\":\"integer\",\"description\":\"Search radius in tiles from anchor colonist/base center (default: 25, max: 60).\"},\"target_name\":{\"type\":\"string\",\"description\":\"Optional filter for specific defName or label (e.g. 'Plant_Berry', 'Plant_TreeOak', 'MineableSteel').\"},\"max_count\":{\"type\":\"integer\",\"description\":\"Maximum number of items to designate in one call (default: 15, max: 50).\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to center search and check autonomy permissions.\"}},\"required\":[\"designation_type\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            var argsRoot = TryParseArgumentsObject(args.ArgumentsJson);
            if (!TryGetArgument<string>(argsRoot, "designation_type", out var designationTypeStr)
                || string.IsNullOrWhiteSpace(designationTypeStr))
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, "Missing required parameter 'designation_type' (Harvest, ChopWood, Mine, or Hunt).")));
            }

#if RIMMIND_ACTIONS_TESTS
            var domain = ResolveAutonomyDomain(argsRoot, args)
                ?? AutonomyDomain.ResourceMarking;
            if (TryGetArgument<int>(argsRoot, "pawn_id", out int testPawnId) && testPawnId > 0)
            {
                var guard = GuardColonistTarget(testPawnId, domain, args);
                if (guard.IsErr)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(guard.Error));
                }
            }
            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok($"Designated {designationTypeStr}", args.ToolCallId, Id)));
#else
            var actorResolution = ActionPermissionGuard.ResolveActorAndMap(args, "pawn_id", fallbackToAnyActiveColonist: true, parsedArgs: argsRoot);
            if (actorResolution.Error != null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, actorResolution.Error)));
            }

            var map = actorResolution.Map;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for resource designation.")));
            }

            // 1. Resolve Colonist Pawn and Permission Check
            Pawn? pawn = actorResolution.Pawn;

            var domain = ResolveAutonomyDomain(argsRoot, args)
                ?? AutonomyDomain.ResourceMarking;
            var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, domain, args);
            if (!permCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
            }

            // 2. Resolve Center Cell, Radius, Count Limit, and Filter
            IntVec3 centerCell = pawn?.Position ?? map.Center;

            TryGetArgument<int>(argsRoot, "radius", out var radius);
            if (radius <= 0) radius = 60;
            radius = Math.Max(5, Math.Min(120, radius));

            TryGetArgument<int>(argsRoot, "max_count", out var maxCount);
            if (maxCount <= 0) maxCount = 15;
            maxCount = Math.Max(1, Math.Min(50, maxCount));

            TryGetArgument<string>(argsRoot, "target_name", out var targetNameFilter);
            if (string.IsNullOrWhiteSpace(targetNameFilter))
            {
                TryGetArgument<string>(argsRoot, "target", out targetNameFilter);
            }
            targetNameFilter = targetNameFilter?.Trim();
            string rawTarget = targetNameFilter ?? string.Empty;
            bool targetImpliesTrees =
                rawTarget.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawTarget.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawTarget.IndexOf("\u6811", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawTarget.IndexOf("\u6728", StringComparison.OrdinalIgnoreCase) >= 0;

            if (IsGenericResourceFilter(targetNameFilter))
            {
                targetNameFilter = null;
            }

            // 3. Resolve DesignationDef and Base Eligibility Predicate (without targetNameFilter)
            string mode = designationTypeStr.Trim().ToLowerInvariant();
            DesignationDef desDef;
            bool isMineMode = false;
            bool isHarvestNonTreeMode = false;
            Func<Thing, bool> basePredicate;

            if (mode.Contains("harvest") && !targetImpliesTrees)
            {
                desDef = DesignationDefOf.HarvestPlant;
                isHarvestNonTreeMode = true;
                basePredicate = t =>
                    t is Plant plant
                    && plant.def?.plant != null
                    && !plant.def.plant.IsTree
                    && plant.def.plant.Harvestable
                    && plant.HarvestableNow
                    && map.designationManager.DesignationOn(plant, desDef) == null;
            }
            else if (mode.Contains("chop") || mode.Contains("wood") || mode.Contains("tree") || targetImpliesTrees)
            {
                desDef = DesignationDefOf.HarvestPlant;
                basePredicate = t =>
                    t is Plant plant
                    && plant.def?.plant != null
                    && plant.def.plant.IsTree
                    && plant.HarvestableNow
                    && map.designationManager.DesignationOn(plant, desDef) == null;
            }
            else if (mode.Contains("cut") || mode.Contains("clear"))
            {
                desDef = DesignationDefOf.CutPlant;
                basePredicate = t =>
                    t is Plant plant
                    && !(map.zoneManager?.ZoneAt(plant.Position) is Zone_Growing)
                    && !(plant.Position.GetEdifice(map) is Building_PlantGrower)
                    && map.designationManager.DesignationOn(plant, desDef) == null;
            }
            else if (mode.Contains("mine") || mode.Contains("ore") || mode.Contains("mineral"))
            {
                desDef = DesignationDefOf.Mine;
                isMineMode = true;
                basePredicate = t =>
                    (t.def?.mineable == true || t is Mineable)
                    && (desDef.targetType == TargetType.Cell
                        ? map.designationManager.DesignationAt(t.Position, desDef)
                        : map.designationManager.DesignationOn(t, desDef)) == null;
            }
            else if (mode.Contains("hunt") || mode.Contains("meat") || mode.Contains("animal"))
            {
                desDef = DesignationDefOf.Hunt;
                basePredicate = t =>
                    t is Pawn animal
                    && !animal.Dead
                    && !animal.Downed
                    && animal.RaceProps != null
                    && animal.RaceProps.Animal
                    && !animal.RaceProps.Humanlike
                    && animal.Faction == null
                    && !animal.RaceProps.predator
                    && !animal.InAggroMentalState
                    && animal.RaceProps.manhunterOnDamageChance <= 0.1f
                    && animal.RaceProps.baseBodySize <= 1.5f
                    && map.designationManager.DesignationOn(animal, desDef) == null;
            }
            else
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, $"Unknown designation type '{designationTypeStr}'. Supported: 'Harvest', 'ChopWood', 'Mine', 'Hunt'.")));
            }

            // 4. Single-pass bucketed scan over map.listerThings.AllThings
            //    Bucket 1: within requested radius + matches targetNameFilter
            //    Bucket 2: within expanded 120-tile radius + matches targetNameFilter
            //    Bucket 3: within expanded 120-tile radius + unfiltered fallback (when targetNameFilter matched nothing)
            //    Bucket 4: winter harvest fallback (mature trees when 0 wild edible plants exist)
            var inRadiusFiltered = new List<Thing>();
            var mapWideFiltered = new List<Thing>();
            var mapWideUnfiltered = new List<Thing>();
            var winterTreeFallback = new List<Thing>();

            bool hasFilter = !string.IsNullOrWhiteSpace(targetNameFilter);
            int expandedRadius = Math.Max(radius, 120);
            var allSpawnedThings = map.listerThings?.AllThings;

            if (allSpawnedThings != null)
            {
                for (int i = 0; i < allSpawnedThings.Count; i++)
                {
                    var thing = allSpawnedThings[i];
                    if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                    if (thing.Position.Fogged(map)) continue;

                    bool inExpanded = thing.Position.InHorDistOf(centerCell, expandedRadius);
                    if (inExpanded && basePredicate(thing))
                    {
                        bool inPrimary = thing.Position.InHorDistOf(centerCell, radius);
                        bool unfilteredMineAllowed = !isMineMode || thing.def?.building?.isResourceRock == true;

                        if (!hasFilter)
                        {
                            if (unfilteredMineAllowed)
                            {
                                if (inPrimary) inRadiusFiltered.Add(thing);
                                else mapWideFiltered.Add(thing);
                            }
                        }
                        else
                        {
                            if (MatchesTargetFilter(thing, targetNameFilter))
                            {
                                if (inPrimary) inRadiusFiltered.Add(thing);
                                else mapWideFiltered.Add(thing);
                            }
                            else if (unfilteredMineAllowed)
                            {
                                mapWideUnfiltered.Add(thing);
                            }
                        }
                    }
                    else if (isHarvestNonTreeMode
                             && thing is Plant tree
                             && tree.def?.plant != null
                             && tree.def.plant.IsTree
                             && tree.HarvestableNow
                             && map.designationManager.DesignationOn(tree, DesignationDefOf.HarvestPlant) == null)
                    {
                        winterTreeFallback.Add(tree);
                    }
                }
            }

            List<Thing> candidates = inRadiusFiltered.Count > 0 ? inRadiusFiltered
                : mapWideFiltered.Count > 0 ? mapWideFiltered
                : (hasFilter && mapWideUnfiltered.Count > 0) ? mapWideUnfiltered
                : (isHarvestNonTreeMode && winterTreeFallback.Count > 0) ? winterTreeFallback
                : inRadiusFiltered;

            // Note: this tool intentionally never unforbids items. Forbid state is a player decision.
            if (candidates.Count == 0)
            {
                int existingDesignations = map.designationManager?.AllDesignations?.Count(d => d.def == desDef) ?? 0;
                if (existingDesignations > 0)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                        ToolResult.Ok(
                            ActionPermissionGuard.TranslateOrFallback(
                                "RimMind.Actions.Designate.NoNewTargets",
                                "No new undesignated '{0}' targets found near ({1}, {2}) (existing active designations: {3}).",
                                designationTypeStr,
                                centerCell.x,
                                centerCell.z,
                                existingDesignations),
                            args.ToolCallId,
                            Id)));
                }
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(
                        Id,
                        ActionPermissionGuard.TranslateOrFallback(
                            "RimMind.Actions.Designate.NoTargetsInRadius",
                            "No undesignated '{0}' targets found within {1} tiles of ({2}, {3}).",
                            designationTypeStr,
                            radius,
                            centerCell.x,
                            centerCell.z))));
            }

            // 5. Sort by Edible Food Priority & Distance and Apply Designation
            var selected = candidates
                .OrderByDescending(t =>
                {
                    if (!isHarvestNonTreeMode || t is not Plant p) return 0;
                    var hDef = p.def?.plant?.harvestedThingDef;
                    return (hDef != null && hDef.IsNutritionGivingIngestible && hDef.ingestible?.HumanEdible == true) ? 1 : 0;
                })
                .ThenBy(t => t.Position.DistanceToSquared(centerCell))
                .Take(maxCount)
                .ToList();

            int addedCount = 0;
            foreach (var target in selected)
            {
                map.designationManager.AddDesignation(desDef.targetType == TargetType.Cell
                    ? new Designation(target.Position, desDef)
                    : new Designation(target, desDef));
                addedCount++;
            }

            // Respect player work tab: NEVER call workSettings.SetPriority.
            // If the acting colonist is already assigned to PlantCutting, immediately dispatch the first edible harvest job.
            if (isHarvestNonTreeMode
                && pawn != null
                && !pawn.Dead
                && !pawn.Downed
                && !pawn.Drafted
                && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting)
                && (pawn.workSettings == null || pawn.workSettings.WorkIsActive(WorkTypeDefOf.PlantCutting))
                && selected.Count > 0
                && selected[0] is Plant firstPlant
                && firstPlant.HarvestableNow)
            {
                var plantsCutGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantsCut");
                var harvestJob = JobMaker.MakeJob(JobDefOf.HarvestDesignated, new LocalTargetInfo(firstPlant));
                harvestJob.workGiverDef = plantsCutGiver;
                pawn.jobs?.StartJob(harvestJob, Verse.AI.JobCondition.InterruptForced);
            }

            string targetSample = string.Join(", ", selected.Take(3).Select(t => t.LabelShort));
            if (selected.Count > 3) targetSample += $" (+{addedCount - 3} more)";

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok(
                    ActionPermissionGuard.TranslateOrFallback(
                        "RimMind.Actions.Designate.DesignateSuccess",
                        "Successfully designated {0} '{1}' target(s) ({2}). Colonists will execute via work priorities.",
                        addedCount,
                        desDef.label,
                        targetSample),
                    args.ToolCallId,
                    Id)));
#endif
        }

#if !RIMMIND_ACTIONS_TESTS
        private static bool MatchesTargetFilter(Thing thing, string? targetNameFilter)
        {
            if (string.IsNullOrWhiteSpace(targetNameFilter))
                return true;

            string filter = targetNameFilter!;
            bool matchesDef = thing.def?.defName != null
                && thing.def.defName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchesLabel = thing.Label != null
                && thing.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            return matchesDef || matchesLabel;
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
#endif
    }
}
