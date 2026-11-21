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
        /// In-game evaluation of map conditions against safety guard.
        /// </summary>
        public static Result<Unit, RimMindError> CheckConstructionSafety(Map? map, ThingDef? buildingDef, ThingDef? stuffDef)
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

            // 2. Resource reserve check
            int currentWood = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
            int currentSteel = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);
            string? stuffName = stuffDef?.defName;

            return ResourceSafetyPolicy.EvaluateReserves(currentWood, currentSteel, stuffName);
        }
    }
}
