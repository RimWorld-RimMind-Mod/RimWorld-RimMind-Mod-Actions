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
using RimMind.Presentation.Api;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool that autonomously plans and executes an integrated micro-room construction project
    /// (e.g. 5x5 emergency shelter: 4 walls + 1 door + 1 bed + 1 torch)
    /// binding all sub-blueprints into a unified AgentIntentSession.
    /// </summary>
    public sealed class PlanRoomProjectCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_plan_room_project";

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Autonomously plans and places a composite micro-room project (e.g. 5x5 shelter: 4 walls, 1 door, 1 bed, 1 torch) and chains a multi-step construction lease on the colony builder.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"room_type\":{\"type\":\"string\",\"description\":\"Type of room to plan: 'Shelter' (emergency shelter, default), 'Bedroom', or 'Storage'.\"},\"size\":{\"type\":\"integer\",\"description\":\"Outer room square dimension in tiles (default: 5, range: 4-9).\"},\"stuff_def\":{\"type\":\"string\",\"description\":\"Wall building material (e.g. 'WoodLog', 'Steel'). If omitted, uses most available material.\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to assign as lead builder.\"},\"include_bed\":{\"type\":\"boolean\",\"description\":\"Whether to include a bed inside the room (default: true).\"}},\"required\":[]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            var actorResolution = ActionPermissionGuard.ResolveActorAndMap(args, "pawn_id", fallbackToAnyActiveColonist: false);
            if (actorResolution.Error != null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, actorResolution.Error)));
            }

            var map = actorResolution.Map;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for micro-room project planning.")));
            }

            // 1. Resolve Builder Colonist
            Pawn? builderPawn = actorResolution.Pawn;
            if (builderPawn == null)
            {
                // Pick active colonist with highest construction skill
                builderPawn = RimMind.Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                    .Where(p => RimMind.Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed)
                    .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0)
                    .FirstOrDefault();
            }

            // 2. Resolve Room Type & Autonomy Permission Guard
            TryGetArgument<string>(args.ArgumentsJson, "room_type", out var roomType);
            if (string.IsNullOrWhiteSpace(roomType)) roomType = "Shelter";

            bool isSurvivalRoom = roomType.Equals("Shelter", StringComparison.OrdinalIgnoreCase)
                               || roomType.Equals("Bedroom", StringComparison.OrdinalIgnoreCase);
            var roomAutonomyDomain = isSurvivalRoom
                ? AutonomyDomain.EmergencySurvival
                : AutonomyDomain.BlueprintPlacement;

            var permCheck = ActionPermissionGuard.CheckPermission(builderPawn, Id, roomAutonomyDomain, args);
            if (!permCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
            }

            // 3. Resolve Size and Materials
            TryGetArgument<int>(args.ArgumentsJson, "size", out var size);
            if (size <= 0) size = 5;
            size = Math.Clamp(size, 4, 9);

            TryGetArgument<string>(args.ArgumentsJson, "stuff_def", out var stuffDefName);
            ThingDef? selectedStuff = null;
            if (!string.IsNullOrWhiteSpace(stuffDefName))
            {
                selectedStuff = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName.Trim());
            }
            if (selectedStuff == null)
            {
                int woodCount = ResourceSafetyGuard.GetAvailableCount(map, ThingDefOf.WoodLog);
                int steelCount = ResourceSafetyGuard.GetAvailableCount(map, ThingDefOf.Steel);
                selectedStuff = woodCount >= steelCount ? ThingDefOf.WoodLog : ThingDefOf.Steel;
            }

            // 4. Resource Safety Guard Check
            var safetyCheck = ResourceSafetyGuard.CheckConstructionSafety(map, ThingDefOf.Wall, selectedStuff);
            if (!safetyCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(safetyCheck.Error));
            }

            // 5. Search for Clear Square Region (fallback from builder position to map.Center)
            IntVec3 origin = (builderPawn != null && builderPawn.Position.InHorDistOf(map.Center, 28f)) ? builderPawn.Position : map.Center;
            CellRect bestRect = default;
            bool foundValidSpot = TryFindClearRoomRect(map, origin, size, selectedStuff, out bestRect);
            if (!foundValidSpot && origin != map.Center)
            {
                foundValidSpot = TryFindClearRoomRect(map, map.Center, size, selectedStuff, out bestRect);
            }

            if (!foundValidSpot)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id,
                        ActionPermissionGuard.TranslateOrFallback(
                            "RimMind.Actions.Room.SiteSelectionFailed",
                            "[Site Selection Failed] No open flat buildable area found nearby for a {0}x{0} room.",
                            size))));
            }

            // 6. Place Blueprints
            var placedBlueprints = new List<Blueprint>();
            IntVec3 doorCell = new IntVec3(bestRect.minX + size / 2, 0, bestRect.minZ);

            // Perimeter walls and door
            foreach (var cell in bestRect.EdgeCells)
            {
                if (cell == doorCell)
                {
                    var doorBp = GenConstruct.PlaceBlueprintForBuild(
                        ThingDefOf.Door, cell, map, Rot4.North, Faction.OfPlayer, selectedStuff);
                    if (doorBp != null) placedBlueprints.Add(doorBp);
                }
                else
                {
                    var wallBp = GenConstruct.PlaceBlueprintForBuild(
                        ThingDefOf.Wall, cell, map, Rot4.North, Faction.OfPlayer, selectedStuff);
                    if (wallBp != null) placedBlueprints.Add(wallBp);
                }
            }

            // Interior Bed
            TryGetArgument<bool>(args.ArgumentsJson, "include_bed", out var includeBed);
            if (!args.ArgumentsJson.Contains("\"include_bed\""))
            {
                includeBed = !string.Equals(roomType, "Storage", StringComparison.OrdinalIgnoreCase);
            }
            if (includeBed)
            {
                IntVec3 bedCell = new IntVec3(bestRect.minX + 1, 0, bestRect.maxZ - 1);
                var bedBp = GenConstruct.PlaceBlueprintForBuild(
                    ThingDefOf.Bed, bedCell, map, Rot4.South, Faction.OfPlayer, selectedStuff);
                if (bedBp != null) placedBlueprints.Add(bedBp);
            }

            // Interior Torch
            var torchDef = DefDatabase<ThingDef>.GetNamedSilentFail("TorchLamp")
                        ?? DefDatabase<ThingDef>.GetNamedSilentFail("Campfire");
            if (torchDef != null)
            {
                IntVec3 torchCell = new IntVec3(bestRect.maxX - 1, 0, bestRect.maxZ - 1);
                var torchBp = GenConstruct.PlaceBlueprintForBuild(
                    torchDef, torchCell, map, Rot4.North, Faction.OfPlayer, null);
                if (torchBp != null) placedBlueprints.Add(torchBp);
            }

            // 7. Chain Unified AgentIntentSession on Builder Colonist
            bool sessionChained = false;
            string builderName = builderPawn?.LabelShort ?? "Colonist";

            if (builderPawn != null && placedBlueprints.Count > 0)
            {
                var firstBp = placedBlueprints[0];
                Map projectMap = map;
                var targetCells = placedBlueprints.Select(b => b.Position).Distinct().ToList();
                sessionChained = RimMindAPI.Agent.StartIntentSession(
                    pawn: builderPawn,
                    intentId: $"Project_Shelter_{bestRect.CenterCell.x}_{bestRect.CenterCell.z}",
                    startTick: Find.TickManager?.TicksGame ?? 0,
                    durationTicks: 5000,
                    priority: 7.8f,
                    target: firstBp,
                    jobProvider: p =>
                    {
                        if (p == null || projectMap == null) return null;
                        // Respect the player's work tab: never rewrite priorities.
                        if (p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                            || (p.workSettings != null && !p.workSettings.WorkIsActive(WorkTypeDefOf.Construction)))
                        {
                            return null;
                        }

                        var deliverBpGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints")?.Worker as WorkGiver_Scanner
                            ?? new WorkGiver_ConstructDeliverResourcesToBlueprints();
                        var deliverFrameGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToFrames")?.Worker as WorkGiver_Scanner
                            ?? new WorkGiver_ConstructDeliverResourcesToFrames();
                        var finishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames")?.Worker as WorkGiver_Scanner
                            ?? new WorkGiver_ConstructFinishFrames();

                        foreach (var bp in placedBlueprints)
                        {
                            if (bp == null || bp.Destroyed || !bp.Spawned) continue;
                            if (!p.CanReserveAndReach(bp, PathEndMode.Touch, Danger.Some)) continue;

                            var haulJob = deliverBpGiver.JobOnThing(p, bp, true);
                            if (haulJob != null) return haulJob;

                            var finishBpJob = finishGiver.JobOnThing(p, bp, true);
                            if (finishBpJob != null) return finishBpJob;
                        }

                        foreach (var cell in targetCells)
                        {
                            if (!cell.InBounds(projectMap)) continue;
                            var frame = cell.GetThingList(projectMap).OfType<Frame>().FirstOrDefault(f => !f.Destroyed && f.Spawned);
                            if (frame == null || !p.CanReserveAndReach(frame, PathEndMode.Touch, Danger.Some)) continue;

                            var deliverJob = deliverFrameGiver.JobOnThing(p, frame, true);
                            if (deliverJob != null) return deliverJob;

                            var finishJob = finishGiver.JobOnThing(p, frame, true);
                            if (finishJob != null) return finishJob;
                        }

                        return null;
                    },
                    completionPredicate: p =>
                        projectMap == null
                        || (placedBlueprints.All(b => b == null || b.Destroyed || !b.Spawned)
                            && !targetCells.Any(cell => cell.InBounds(projectMap)
                                && cell.GetThingList(projectMap).OfType<Frame>().Any(f => !f.Destroyed && f.Spawned))));
            }

            string planMsg = ActionPermissionGuard.TranslateOrFallback(
                "RimMind.Actions.Room.PlanSuccess",
                "Successfully planned and placed {2} room blueprints ({3}x{3} walls/door/bed/light) centered at ({0}, {1}).",
                bestRect.CenterCell.x, bestRect.CenterCell.z, placedBlueprints.Count, size);
            if (sessionChained)
            {
                planMsg += ActionPermissionGuard.TranslateOrFallback(
                    "RimMind.Actions.Room.LeaseChained",
                    " Chained continuous construction lease on builder {0}.",
                    builderName);
            }

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok(
                    planMsg,
                    args.ToolCallId,
                    Id)));
        }

        private static bool TryFindClearRoomRect(Map map, IntVec3 origin, int size, ThingDef? selectedStuff, out CellRect bestRect)
        {
            bestRect = default;
            foreach (var offset in GenRadial.RadialCellsAround(origin, 30f, true))
            {
                var testRect = new CellRect(offset.x, offset.z, size, size);
                if (!testRect.FullyContainedWithin(new CellRect(0, 0, map.Size.x, map.Size.z))) continue;

                bool valid = true;
                foreach (var cell in testRect)
                {
                    if (cell.Fogged(map)) { valid = false; break; }
                    if (!cell.Standable(map) && cell.GetEdifice(map) != null) { valid = false; break; }

                    var things = map.thingGrid.ThingsListAt(cell);
                    if (things != null && things.Any(t => t.def.passability == Traversability.Impassable))
                    {
                        valid = false;
                        break;
                    }

                    var terrain = cell.GetTerrain(map);
                    if (terrain != null && terrain.IsWater)
                    {
                        valid = false;
                        break;
                    }

                    var acceptance = GenConstruct.CanPlaceBlueprintAt(ThingDefOf.Wall, cell, Rot4.North, map, false, null, null, selectedStuff);
                    if (!acceptance.Accepted)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    bestRect = testRect;
                    return true;
                }
            }
            return false;
        }
    }
}
