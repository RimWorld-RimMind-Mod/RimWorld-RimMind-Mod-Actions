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
using Verse.AI;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool that autonomously directs colonists to equip protective gear or thermal clothing
    /// (e.g. wearing parkas/tuques when cold, armor vests during raids) for personal survival.
    /// </summary>
    public sealed class ManageEquipmentCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_manage_equipment";

        public override ToolDefinition Definition => new ToolDefinition
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
            var map = Find.CurrentMap;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for equipment management.")));
            }

            // 1. Resolve Colonist Pawn & Autonomy Guard
            Pawn? pawn = ActionPermissionGuard.ResolveColonistOnMap(map, args, "pawn_id", fallbackToAnyActiveColonist: true);

            if (pawn == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, "Colonist pawn not found or invalid.")));
            }

            var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, AutonomyDomain.EmergencySurvival, args);
            if (!permCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
            }

            if (pawn.Dead || pawn.Downed || pawn.apparel == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                    ToolResult.Ok(
                        ActionPermissionGuard.TranslateOrFallback(
                            "RimMind.Actions.Equipment.DownedOrUnable",
                            "Colonist {0} is currently downed or unable to change apparel.",
                            pawn.LabelShort),
                        args.ToolCallId, Id)));
            }

            // 2. Evaluate Purpose and Conditions
            TryGetArgument<string>(args.ArgumentsJson, "purpose", out var purposeStr);
            if (string.IsNullOrWhiteSpace(purposeStr)) purposeStr = "Auto";

            TryGetArgument<string>(args.ArgumentsJson, "target_apparel_def", out var targetApparelDefName);

            float ambientTemp = pawn.Position.GetTemperature(map);
            float minComfy = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin);
            float maxComfy = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax);

            // 3. Scan Available Apparel on Map
            var allApparel = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel)
                .OfType<Apparel>()
                .Where(a => a.Spawned && !a.Destroyed && !a.IsForbidden(pawn)
                         && ApparelUtility.HasPartsToWear(pawn, a.def)
                         && pawn.CanReserveAndReach(a, PathEndMode.ClosestTouch, Danger.Some))
                .ToList();

            if (!string.IsNullOrWhiteSpace(targetApparelDefName))
            {
                allApparel = allApparel.Where(a =>
                    a.def.defName.Equals(targetApparelDefName.Trim(), StringComparison.OrdinalIgnoreCase)
                    || a.Label.Contains(targetApparelDefName.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            bool CanWearApparel(Apparel a) =>
                pawn.apparel.WornApparel == null || pawn.apparel.WornApparel.All(worn => ApparelUtility.CanWearTogether(a.def, worn.def, pawn.RaceProps.body));

            Apparel? bestApparel = null;

            if (ambientTemp < minComfy + 5f || purposeStr.Equals("Thermal", StringComparison.OrdinalIgnoreCase))
            {
                // Prioritize cold insulation (e.g. Parka, Tuque, Duster)
                bestApparel = allApparel
                    .OrderByDescending(a => a.GetStatValue(StatDefOf.Insulation_Cold))
                    .FirstOrDefault(a => a.GetStatValue(StatDefOf.Insulation_Cold) > 5f
                                      && CanWearApparel(a));

                if (bestApparel == null)
                {
                    // Allow dropping existing worse apparel if significantly warmer
                    bestApparel = allApparel
                        .OrderByDescending(a => a.GetStatValue(StatDefOf.Insulation_Cold))
                        .FirstOrDefault(a => a.GetStatValue(StatDefOf.Insulation_Cold) > 10f);
                }
            }
            else if (purposeStr.Equals("Armor", StringComparison.OrdinalIgnoreCase))
            {
                // Prioritize sharp armor
                bestApparel = allApparel
                    .OrderByDescending(a => a.GetStatValue(StatDefOf.ArmorRating_Sharp))
                    .FirstOrDefault(a => a.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.3f);
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
                        args.ToolCallId,
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
                            pawn.LabelShort, bestApparel.LabelCap))));
            }

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok(
                    ActionPermissionGuard.TranslateOrFallback(
                        "RimMind.Actions.Equipment.WearJobSuccess",
                        "[Autonomous Apparel] Colonist {0} (temp: {1}°C) ordered to wear {2}.",
                        pawn.LabelShort, $"{ambientTemp:F1}", bestApparel.LabelCap),
                    args.ToolCallId,
                    Id)));
        }
    }
}
