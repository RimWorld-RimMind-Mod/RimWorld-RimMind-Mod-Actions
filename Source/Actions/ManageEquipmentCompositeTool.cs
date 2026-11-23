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
using Verse.AI;
#endif
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool that autonomously directs colonists to equip protective gear or thermal clothing
    /// (e.g. wearing parkas/tuques when cold, armor vests during raids) for personal survival.
    /// </summary>
    public sealed class ManageEquipmentCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_manage_equipment";

        private static readonly string[] Aliases = new[]
        {
            "manage_equipment",
            "equip_combat_gear",
            "actions.manage_equipment",
            "actions.equip_combat_gear",
            "actions_equip_combat_gear"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "target_apparel_def",
            "purpose"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.EmergencySurvival;
        protected override ToolScope? ToolScopes => ToolScope.ColonistSelf;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => true;
        protected override string? DisplayName => "Manage Equipment";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Autonomously equips protective gear or thermal apparel (e.g. wearing parkas/tuques when cold, armor vests during threat) for colonist survival.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\",\"description\":\"Target colonist pawn ID to equip gear on.\"},\"purpose\":{\"type\":\"string\",\"description\":\"Equip purpose: 'Thermal' (cold/heat protection), 'Armor' (combat protection), or 'Auto' (evaluates threat and ambient temperature, default).\"},\"target_apparel_def\":{\"type\":\"string\",\"description\":\"Optional specific apparel defName to wear (e.g. 'Apparel_Parka', 'Apparel_Tuque', 'Apparel_FlakVest').\"}},\"required\":[\"pawn_id\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            return ExecuteDirectToolAsync(args, (argsRoot, effectiveArgs, _) =>
            {
#if RIMMIND_ACTIONS_TESTS
                var pawnIdResult = TryGetPawnIdOrError(argsRoot, effectiveArgs, "pawn_id");
                if (pawnIdResult.IsErr)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(pawnIdResult.Error));
                }
                var domain = ResolveAutonomyDomain(argsRoot, effectiveArgs)
                    ?? AutonomyDomain.EmergencySurvival;
                var guard = GuardColonistTarget(pawnIdResult.Value, domain, effectiveArgs);
                if (guard.IsErr)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(guard.Error));
                }
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("ok", effectiveArgs.ToolCallId, Id)));
#else
                var actorResolution = ActionPermissionGuard.ResolveActorAndMap(effectiveArgs, "pawn_id", fallbackToAnyActiveColonist: true, parsedArgs: argsRoot);
                if (actorResolution.Error != null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, actorResolution.Error) with { TraceId = effectiveArgs.TraceId }));
                }

                var map = actorResolution.Map;
                if (map == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal("No active map available for equipment management.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 1. Resolve Colonist Pawn & Autonomy Guard
                Pawn? pawn = actorResolution.Pawn;

                if (pawn == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, "Colonist pawn not found or invalid.") with { TraceId = effectiveArgs.TraceId }));
                }

                var domain = ResolveAutonomyDomain(argsRoot, effectiveArgs)
                    ?? AutonomyDomain.EmergencySurvival;
                var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, domain, effectiveArgs);
                if (!permCheck.IsOk)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
                }

                if (pawn.Dead || pawn.Downed || pawn.apparel == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id,
                            ActionPermissionGuard.TranslateOrFallback(
                                "RimMind.Actions.Equipment.DownedOrUnable",
                                "Colonist {0} is currently downed or unable to change apparel.",
                                pawn.LabelShort)) with { TraceId = effectiveArgs.TraceId }));
                }

                // 2. Evaluate Purpose and Conditions
                TryGetArgument<string>(argsRoot, "purpose", out var purposeStr);
                if (string.IsNullOrWhiteSpace(purposeStr)) purposeStr = "Auto";
                string normalizedPurpose = purposeStr.Trim();

                TryGetArgument<string>(argsRoot, "target_apparel_def", out var targetApparelDefName);

                float ambientTemp = pawn.Position.GetTemperature(map);
                float minComfy = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin);
                float maxComfy = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax);

                // 3. Scan Available Apparel on Map
                var allApparel = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel)
                    .OfType<Apparel>()
                    .Where(a => a.Spawned && !a.Destroyed && !a.Position.Fogged(map) && !a.IsForbidden(pawn)
                             && ApparelUtility.HasPartsToWear(pawn, a.def)
                             && pawn.CanReserveAndReach(a, PathEndMode.ClosestTouch, Danger.Some))
                    .ToList();

                if (!string.IsNullOrWhiteSpace(targetApparelDefName))
                {
                    var trimmedApparel = targetApparelDefName.Trim();
                    allApparel = allApparel.Where(a =>
                        string.Equals(a.def?.defName, trimmedApparel, StringComparison.OrdinalIgnoreCase)
                        || (a.Label != null && a.Label.IndexOf(trimmedApparel, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();
                }

                bool CanWearApparel(Apparel a) =>
                    pawn.apparel.WornApparel == null || pawn.apparel.WornApparel.All(worn => ApparelUtility.CanWearTogether(a.def, worn.def, pawn.RaceProps.body));

                Apparel? PickThermalApparel(bool hot)
                {
                    var stat = hot ? StatDefOf.Insulation_Heat : StatDefOf.Insulation_Cold;
                    var candidate = allApparel
                        .OrderByDescending(a => a.GetStatValue(stat))
                        .FirstOrDefault(a => a.GetStatValue(stat) > 5f && CanWearApparel(a));

                    if (candidate == null)
                    {
                        // Allow replacing worse apparel when significantly better insulated
                        candidate = allApparel
                            .OrderByDescending(a => a.GetStatValue(stat))
                            .FirstOrDefault(a => a.GetStatValue(stat) > 10f);
                    }

                    return candidate;
                }

                Apparel? bestApparel = null;

                if (normalizedPurpose.Equals("Armor", StringComparison.OrdinalIgnoreCase))
                {
                    // Explicit Armor request takes precedence over ambient temperature heuristics
                    bestApparel = allApparel
                        .OrderByDescending(a => a.GetStatValue(StatDefOf.ArmorRating_Sharp))
                        .FirstOrDefault(a => a.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.3f);
                }
                else if (normalizedPurpose.Equals("Thermal", StringComparison.OrdinalIgnoreCase))
                {
                    bool preferHeat = ambientTemp > maxComfy - 5f && ambientTemp >= minComfy + 5f;
                    bestApparel = PickThermalApparel(hot: preferHeat);
                }
                else if (ambientTemp < minComfy + 5f)
                {
                    bestApparel = PickThermalApparel(hot: false);
                }
                else if (ambientTemp > maxComfy - 5f)
                {
                    bestApparel = PickThermalApparel(hot: true);
                }
                else
                {
                    // Auto: general check
                    bestApparel = allApparel.FirstOrDefault(a => CanWearApparel(a));
                }

                if (bestApparel == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                        ToolResult.Ok(
                            ActionPermissionGuard.TranslateOrFallback(
                                "RimMind.Actions.Equipment.NoSuitableApparel",
                                "Colonist {0} (ambient: {1}°C, comfy: {2}~{3}°C) found no suitable replacement apparel in storage.",
                                pawn.LabelShort, $"{ambientTemp:F1}", $"{minComfy:F1}", $"{maxComfy:F1}"),
                            effectiveArgs.ToolCallId,
                            Id)));
                }

                // 4. Issue Wear Job
                Job job = JobMaker.MakeJob(JobDefOf.Wear, bestApparel);
                bool success = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                if (!success)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id,
                            ActionPermissionGuard.TranslateOrFallback(
                                "RimMind.Actions.Equipment.WearJobFailed",
                                "Failed to issue wear job for {1} on colonist {0}.",
                                pawn.LabelShort, bestApparel.LabelCap)) with { TraceId = effectiveArgs.TraceId }));
                }

                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                    ToolResult.Ok(
                        ActionPermissionGuard.TranslateOrFallback(
                            "RimMind.Actions.Equipment.WearJobSuccess",
                            "[Autonomous Apparel] Colonist {0} (temp: {1}°C) ordered to wear {2}.",
                            pawn.LabelShort, $"{ambientTemp:F1}", bestApparel.LabelCap),
                        effectiveArgs.ToolCallId,
                        Id)));
#endif
            }, ct);
        }
    }
}
