using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.Actions.Policies
{
    /// <summary>
    /// Circuit-breaker guard ensuring autonomous agent actions never breach colony survival baselines.
    /// Bridges in-game Map state with pure ResourceSafetyPolicy and Core MapResourceCounter.
    /// </summary>
    public static class ResourceSafetyGuard
    {
        public const int MinWoodReserve = ResourceSafetyPolicy.MinWoodReserve;
        public const int MinSteelReserve = ResourceSafetyPolicy.MinSteelReserve;
        public const int MaxConcurrentBlueprints = ResourceSafetyPolicy.MaxConcurrentBlueprints;

        public static Result<Unit, RimMindError> EvaluateReserves(int availableWood, int availableSteel, string? stuffDefName)
            => ResourceSafetyPolicy.EvaluateReserves(availableWood, availableSteel, stuffDefName);

        public static Result<Unit, RimMindError> EvaluateBlueprintCapacity(int activeBlueprintCount)
            => ResourceSafetyPolicy.EvaluateBlueprintCapacity(activeBlueprintCount);

        /// <summary>
        /// Counts total available unforbidden units of a resource on the map via Core <see cref="MapResourceCounter"/>.
        /// </summary>
        public static int GetAvailableCount(Map? map, ThingDef? def)
            => MapResourceCounter.GetAvailableCount(map, def);

        /// <summary>
        /// In-game evaluation of map conditions against safety guard for a single building placement,
        /// accounting for both <paramref name="stuffDef"/> (<c>costStuffCount</c>) and fixed <c>buildingDef.costList</c>.
        /// </summary>
        public static Result<Unit, RimMindError> CheckConstructionSafety(Map? map, ThingDef? buildingDef, ThingDef? stuffDef)
            => CheckProjectConstructionSafety(map, new[] { (buildingDef, stuffDef, 1) });

        /// <summary>
        /// In-game evaluation of map conditions against safety guard for a multi-blueprint construction project.
        /// Evaluates concurrent blueprint capacity and total WoodLog/Steel requirements across all planned items.
        /// </summary>
        public static Result<Unit, RimMindError> CheckProjectConstructionSafety(
            Map? map,
            IEnumerable<(ThingDef? BuildingDef, ThingDef? StuffDef, int Count)> plannedItems)
        {
            if (map == null) return Result<Unit, RimMindError>.Ok(Unit.Value);

            // 1. Concurrent blueprint check
            if (map.listerThings != null)
            {
                var blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
                int activeCount = blueprints?.Count(b => b.Faction == Faction.OfPlayer) ?? 0;
                var capacityResult = ResourceSafetyPolicy.EvaluateBlueprintCapacity(activeCount);
                if (!capacityResult.IsOk)
                {
                    return capacityResult;
                }
            }

            // 2. Aggregate WoodLog / Steel costs across both stuff costs and fixed costList entries
            int requiredWood = 0;
            int requiredSteel = 0;
            if (plannedItems != null)
            {
                foreach (var (buildingDef, stuffDef, count) in plannedItems)
                {
                    int qty = Math.Max(1, count);
                    if (stuffDef != null)
                    {
                        int stuffUnits = buildingDef != null && buildingDef.MadeFromStuff
                            ? Math.Max(1, buildingDef.costStuffCount)
                            : 1;
                        if (string.Equals(stuffDef.defName, "WoodLog", StringComparison.OrdinalIgnoreCase))
                            requiredWood += stuffUnits * qty;
                        else if (string.Equals(stuffDef.defName, "Steel", StringComparison.OrdinalIgnoreCase))
                            requiredSteel += stuffUnits * qty;
                    }

                    var costList = buildingDef?.costList;
                    if (costList != null)
                    {
                        for (int i = 0; i < costList.Count; i++)
                        {
                            var item = costList[i];
                            if (item?.thingDef == null || item.count <= 0) continue;
                            if (string.Equals(item.thingDef.defName, "WoodLog", StringComparison.OrdinalIgnoreCase))
                                requiredWood += item.count * qty;
                            else if (string.Equals(item.thingDef.defName, "Steel", StringComparison.OrdinalIgnoreCase))
                                requiredSteel += item.count * qty;
                        }
                    }
                }
            }

            int currentWood = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
            int currentSteel = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);

            return ResourceSafetyPolicy.EvaluateMaterialRequirements(
                currentWood,
                currentSteel,
                requiredWood,
                requiredSteel);
        }
    }
}
