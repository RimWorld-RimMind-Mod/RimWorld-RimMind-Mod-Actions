using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimMind.Presentation.Api;
using RimWorld;
#endif
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool that autonomously plans and executes an integrated micro-room construction project
    /// (e.g. 5x5 emergency shelter: 4 walls + 1 door + 1 bed + 1 torch)
    /// binding all sub-blueprints into a unified AgentIntentSession via <see cref="ConstructionIntentChainer"/>.
    /// </summary>
    public sealed class PlanRoomProjectCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_plan_room_project";

        private static readonly string[] Aliases = new[]
        {
            "plan_room_project",
            "actions.plan_room_project"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "room_type"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.BlueprintPlacement;
        protected override ToolScope? ToolScopes => ToolScope.ColonyBuild;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => false;
        protected override string? DisplayName => "Plan Room Project";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Autonomously plans and places a composite micro-room project (e.g. 5x5 shelter: 4 walls, 1 door, 1 bed, 1 torch) and chains a multi-step construction lease on the colony builder.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"room_type\":{\"type\":\"string\",\"description\":\"Type of room to plan: 'Shelter' (emergency shelter, default), 'Bedroom', or 'Storage'.\"},\"size\":{\"type\":\"integer\",\"description\":\"Outer room square dimension in tiles (default: 5, range: 4-9).\"},\"stuff_def\":{\"type\":\"string\",\"description\":\"Wall building material (e.g. 'WoodLog', 'Steel'). If omitted, uses most available material.\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to assign as lead builder.\"},\"include_bed\":{\"type\":\"boolean\",\"description\":\"Whether to include a bed inside the room (default: true).\"}},\"required\":[]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        protected override AutonomyDomain? ResolveAutonomyDomain(JObject? root, ToolCallArgs args)
            => AutonomyDomain.BlueprintPlacement;

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            return ExecuteDirectToolAsync(args, (argsRoot, effectiveArgs, _) =>
            {
#if RIMMIND_ACTIONS_TESTS
                var domain = ResolveAutonomyDomain(argsRoot, effectiveArgs)
                    ?? AutonomyDomain.BlueprintPlacement;
                if (TryGetArgument<int>(argsRoot, "pawn_id", out int testPawnId) && testPawnId > 0)
                {
                    var guard = GuardColonistTarget(testPawnId, domain, effectiveArgs);
                    if (guard.IsErr)
                    {
                        return Task.FromResult(Result<ToolResult, RimMindError>.Err(guard.Error));
                    }
                }
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                    ToolResult.Ok("Planned room", effectiveArgs.ToolCallId, Id)));
#else
                var actorResolution = ActionPermissionGuard.ResolveActorAndMap(effectiveArgs, "pawn_id", fallbackToAnyActiveColonist: false, parsedArgs: argsRoot);
                if (actorResolution.Error != null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, actorResolution.Error) with { TraceId = effectiveArgs.TraceId }));
                }

                var map = actorResolution.Map;
                if (map == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal("No active map available for micro-room project planning.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 1. Resolve Builder Colonist (standing, mentally stable, capable of Construction)
                Pawn? builderPawn = actorResolution.Pawn;
                if (builderPawn == null)
                {
                    builderPawn = RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                        .Where(p => RimMindPawnLookup.IsActiveSpawnedColonist(p)
                                 && !p.Downed
                                 && !p.InMentalState
                                 && !p.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                        .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0)
                        .FirstOrDefault();
                }

                // 2. Resolve Room Type & Autonomy Permission Guard
                TryGetArgument<string>(argsRoot, "room_type", out var roomType);
                if (string.IsNullOrWhiteSpace(roomType)) roomType = "Shelter";

                var permCheck = ActionPermissionGuard.CheckPermission(builderPawn, Id, AutonomyDomain.BlueprintPlacement, effectiveArgs);
                if (!permCheck.IsOk)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
                }

                // 3. Resolve Size, Materials, and Interior Defs
                TryGetArgument<int>(argsRoot, "size", out var size);
                if (size <= 0) size = 5;
                size = Math.Max(4, Math.Min(9, size));

                TryGetArgument<string>(argsRoot, "stuff_def", out var stuffDefName);
                ThingDef? selectedStuff = ConstructionIntentChainer.ResolveStuffForBuilding(ThingDefOf.Wall, stuffDefName, map);
                ThingDef? doorStuff = ConstructionIntentChainer.ResolveStuffForBuilding(ThingDefOf.Door, selectedStuff?.defName, map);

                bool includeBed = TryGetArgument<bool>(argsRoot, "include_bed", out var parsedIncludeBed)
                    ? parsedIncludeBed
                    : !string.Equals(roomType, "Storage", StringComparison.OrdinalIgnoreCase);
                ThingDef? bedStuff = includeBed
                    ? ConstructionIntentChainer.ResolveStuffForBuilding(ThingDefOf.Bed, selectedStuff?.defName, map)
                    : null;

                var torchDef = DefDatabase<ThingDef>.GetNamedSilentFail("TorchLamp")
                            ?? DefDatabase<ThingDef>.GetNamedSilentFail("Campfire");
                if (torchDef != null && (!torchDef.BuildableByPlayer || !torchDef.IsResearchFinished))
                {
                    torchDef = null;
                }

                // 4. Resource Safety Guard Check (full project material cost + blueprint capacity)
                int perimeterCells = 4 * (size - 1);
                var plannedItems = new List<(ThingDef? BuildingDef, ThingDef? StuffDef, int Count)>
                {
                    (ThingDefOf.Wall, selectedStuff, Math.Max(1, perimeterCells - 1)),
                    (ThingDefOf.Door, doorStuff, 1)
                };
                if (includeBed)
                {
                    plannedItems.Add((ThingDefOf.Bed, bedStuff, 1));
                }
                if (torchDef != null)
                {
                    plannedItems.Add((torchDef, null, 1));
                }

                var safetyCheck = ResourceSafetyGuard.CheckProjectConstructionSafety(map, plannedItems);
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
                                size)) with { TraceId = effectiveArgs.TraceId }));
                }

                // 6. Place Blueprints (guarded by CanPlaceBlueprintAt + try/catch)
                var placedBlueprints = new List<Blueprint>();
                IntVec3 doorCell = new IntVec3(bestRect.minX + size / 2, 0, bestRect.minZ);

                void TryPlace(ThingDef def, IntVec3 cell, Rot4 rot, ThingDef? stuff)
                {
                    if (!cell.IsValid || !cell.InBounds(map)) return;
                    if (!GenConstruct.CanPlaceBlueprintAt(def, cell, rot, map, false, null, null, stuff).Accepted)
                        return;
                    try
                    {
                        var bp = GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, Faction.OfPlayer, stuff);
                        if (bp != null) placedBlueprints.Add(bp);
                    }
                    catch
                    {
                        // Best-effort per sub-blueprint; remaining valid cells still proceed.
                    }
                }

                // Perimeter walls and door
                foreach (var cell in bestRect.EdgeCells)
                {
                    if (cell == doorCell)
                    {
                        TryPlace(ThingDefOf.Door, cell, Rot4.North, doorStuff);
                    }
                    else
                    {
                        TryPlace(ThingDefOf.Wall, cell, Rot4.North, selectedStuff);
                    }
                }

                // Interior Bed
                if (includeBed)
                {
                    IntVec3 bedCell = new IntVec3(bestRect.minX + 1, 0, bestRect.maxZ - 1);
                    TryPlace(ThingDefOf.Bed, bedCell, Rot4.South, bedStuff);
                }

                // Interior Torch / Campfire
                if (torchDef != null)
                {
                    IntVec3 torchCell = new IntVec3(bestRect.maxX - 1, 0, bestRect.maxZ - 1);
                    TryPlace(torchDef, torchCell, Rot4.North, null);
                }

                if (placedBlueprints.Count == 0)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal("Unable to place any room blueprints at the selected site.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 7. Chain Unified AgentIntentSession via Shared ConstructionIntentChainer
                bool sessionChained = false;
                string builderName = builderPawn?.LabelShort ?? "Colonist";

                if (builderPawn != null && placedBlueprints.Count > 0)
                {
                    var targetCells = placedBlueprints.Select(b => b.Position).Distinct().ToList();
                    sessionChained = ConstructionIntentChainer.TryChainProject(
                        builderPawn,
                        map,
                        placedBlueprints,
                        targetCells,
                        intentId: $"Project_Shelter_{bestRect.CenterCell.x}_{bestRect.CenterCell.z}",
                        durationTicks: 5000,
                        priority: 7.8f);
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
                        effectiveArgs.ToolCallId,
                        Id)));
#endif
            }, ct);
        }

#if !RIMMIND_ACTIONS_TESTS
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
#endif
    }
}
