using System;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using Verse;
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
            try
            {
                if (!string.IsNullOrEmpty(key) && key.CanTranslate())
                {
                    string template = key.TranslateSimple();
                    return args != null && args.Length > 0 ? string.Format(template, args) : template;
                }
            }
            catch
            {
            }
#endif
            return args != null && args.Length > 0 ? string.Format(fallbackEn, args) : fallbackEn;
        }

        /// <summary>
        /// Pure evaluation of resource reserves against material requirements.
        /// </summary>
        public static Result<Unit, RimMindError> EvaluateReserves(int availableWood, int availableSteel, string? stuffDefName)
        {
            if (string.Equals(stuffDefName, "WoodLog", StringComparison.OrdinalIgnoreCase))
            {
                if (availableWood < MinWoodReserve)
                {
                    return Result<Unit, RimMindError>.Err(
                        RimMindErrors.InvalidAction("resource_safety",
                            TranslateOrFallback(
                                "RimMind.Actions.Safety.WoodReserve",
                                "[Safety Guard] Colony wood reserve is only {0}, below minimum survival threshold (reserve: {1}); construction material consumption blocked!",
                                availableWood,
                                MinWoodReserve)));
                }
            }

            if (string.Equals(stuffDefName, "Steel", StringComparison.OrdinalIgnoreCase))
            {
                if (availableSteel < MinSteelReserve)
                {
                    return Result<Unit, RimMindError>.Err(
                        RimMindErrors.InvalidAction("resource_safety",
                            TranslateOrFallback(
                                "RimMind.Actions.Safety.SteelReserve",
                                "[Safety Guard] Colony steel reserve is only {0}, below minimum survival threshold (reserve: {1}); construction material consumption blocked!",
                                availableSteel,
                                MinSteelReserve)));
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
