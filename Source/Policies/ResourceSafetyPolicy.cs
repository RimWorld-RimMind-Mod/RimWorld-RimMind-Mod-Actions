using System;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

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
                            $"[熔断拦截] 营地木材仅存 {availableWood}，已触发最低生存储备防线 (保留值: {MinWoodReserve})，禁止消耗建材！"));
                }
            }

            if (string.Equals(stuffDefName, "Steel", StringComparison.OrdinalIgnoreCase))
            {
                if (availableSteel < MinSteelReserve)
                {
                    return Result<Unit, RimMindError>.Err(
                        RimMindErrors.InvalidAction("resource_safety",
                            $"[熔断拦截] 营地钢铁仅存 {availableSteel}，已触发最低生存储备防线 (保留值: {MinSteelReserve})，禁止消耗建材！"));
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
                        $"[熔断拦截] 当前营地已有 {activeBlueprintCount} 个进行中的建造蓝图，已达并发上限 ({MaxConcurrentBlueprints})，禁止开工新工程！"));
            }

            return Result<Unit, RimMindError>.Ok(Unit.Value);
        }
    }
}
