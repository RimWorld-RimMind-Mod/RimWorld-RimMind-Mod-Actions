using System;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimMind.Presentation.Api;
#endif

namespace RimMind.Actions.Policies
{
    /// <summary>
    /// Pure policy and threshold logic for colony resource safety and blueprint limits.
    /// </summary>
    public static class ResourceSafetyPolicy
    {
        public const int MinWoodReserve = 80;
        public const int MinSteelReserve = 50;
        public const int MaxConcurrentBlueprints = 2;

        public static string TranslateOrFallback(string key, string fallbackEn, params object[] args)
        {
#if !RIMMIND_ACTIONS_TESTS
            return RimMindAPI.Text.TrOr(key, fallbackEn, args);
#else
            return args != null && args.Length > 0 ? string.Format(fallbackEn, args) : fallbackEn;
#endif
        }

        /// <summary>
        /// Pure evaluation of resource reserves against a single primary stuff material.
        /// </summary>
        public static Result<Unit, RimMindError> EvaluateReserves(int availableWood, int availableSteel, string? stuffDefName)
        {
            bool usesWood = string.Equals(stuffDefName, "WoodLog", StringComparison.OrdinalIgnoreCase);
            bool usesSteel = string.Equals(stuffDefName, "Steel", StringComparison.OrdinalIgnoreCase);
            return EvaluateMaterialRequirements(
                availableWood,
                availableSteel,
                requiredWood: usesWood ? 1 : 0,
                requiredSteel: usesSteel ? 1 : 0);
        }

        /// <summary>
        /// Pure evaluation of resource reserves against total required WoodLog and Steel units
        /// (covering both stuff costs and fixed <c>ThingDef.costList</c> materials, including multi-blueprint projects).
        /// </summary>
        public static Result<Unit, RimMindError> EvaluateMaterialRequirements(
            int availableWood,
            int availableSteel,
            int requiredWood,
            int requiredSteel)
        {
            if (requiredWood > 0)
            {
                int minNeededWood = Math.Max(MinWoodReserve, requiredWood);
                if (availableWood < minNeededWood)
                {
                    return Result<Unit, RimMindError>.Err(
                        RimMindErrors.InvalidAction("resource_safety",
                            TranslateOrFallback(
                                "RimMind.Actions.Safety.WoodReserve",
                                "[Safety Guard] Colony wood reserve is only {0}, below minimum survival threshold (reserve: {1}); construction material consumption blocked!",
                                availableWood,
                                minNeededWood)));
                }
            }

            if (requiredSteel > 0)
            {
                int minNeededSteel = Math.Max(MinSteelReserve, requiredSteel);
                if (availableSteel < minNeededSteel)
                {
                    return Result<Unit, RimMindError>.Err(
                        RimMindErrors.InvalidAction("resource_safety",
                            TranslateOrFallback(
                                "RimMind.Actions.Safety.SteelReserve",
                                "[Safety Guard] Colony steel reserve is only {0}, below minimum survival threshold (reserve: {1}); construction material consumption blocked!",
                                availableSteel,
                                minNeededSteel)));
                }
            }

            return Result<Unit, RimMindError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Pure evaluation of concurrent unfinished blueprints capacity.
        /// </summary>
        public static Result<Unit, RimMindError> EvaluateBlueprintCapacity(int activeBlueprintCount)
        {
            if (activeBlueprintCount >= MaxConcurrentBlueprints)
            {
                return Result<Unit, RimMindError>.Err(
                    RimMindErrors.InvalidAction("resource_safety",
                        TranslateOrFallback(
                            "RimMind.Actions.Safety.BlueprintCap",
                            "[Safety Guard] Colony already has {0} active construction blueprints, reaching concurrency limit ({1}); new projects blocked!",
                            activeBlueprintCount,
                            MaxConcurrentBlueprints)));
            }

            return Result<Unit, RimMindError>.Ok(Unit.Value);
        }
    }
}
