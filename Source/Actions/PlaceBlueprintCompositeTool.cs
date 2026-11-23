using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimMind.Actions.Spatial;
using RimWorld;
#endif
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite action tool that places a building blueprint with smart footprint/interaction-cell placement,
    /// automatic stockpile material detection, and immediate colonist construction intent chaining.
    /// </summary>
    public sealed class PlaceBlueprintCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_place_blueprint";

        private static readonly string[] Aliases = new[]
        {
            "place_blueprint",
            "actions.place_blueprint"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "building_def",
            "stuff_def"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.BlueprintPlacement;
        protected override ToolScope? ToolScopes => ToolScope.ColonyBuild;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => false;
        protected override string? DisplayName => "Place Blueprint";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Places a building blueprint on the map with smart cell placement, material auto-detection, and immediate colonist construction chaining.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"building_def\":{\"type\":\"string\",\"description\":\"DefName of the building to place (e.g. 'Bed', 'PassiveCooler', 'FueledStove', 'Campfire', 'TableShort'). Call actions_query_buildable_structures to inspect valid buildables.\"},\"stuff_def\":{\"type\":\"string\",\"description\":\"Optional material (e.g. 'WoodLog', 'Steel'). If omitted, automatically selects the most abundant valid material in the colony's stockpiles.\"},\"location_hint\":{\"type\":\"string\",\"description\":\"Optional placement preference: 'near_base' (default), 'near_pawn', 'indoor', 'outdoor'.\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to perform construction and anchor location.\"},\"auto_construct\":{\"type\":\"boolean\",\"description\":\"Whether to chain an immediate Intent Session on the colonist to construct the placed blueprint (default: true).\"}},\"required\":[\"building_def\"]}"
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
                if (!TryGetArgument<string>(argsRoot, "building_def", out var buildingDefName)
                    || string.IsNullOrWhiteSpace(buildingDefName))
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, "Missing required parameter 'building_def'.") with { TraceId = effectiveArgs.TraceId }));
                }

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
                    ToolResult.Ok($"Placed {buildingDefName}", effectiveArgs.ToolCallId, Id)));
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
                        RimMindErrors.Internal("No active map available for blueprint placement.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 1. Resolve BuildableDef
                var thingDef = ConstructionIntentChainer.ResolveThingDef(buildingDefName);
                if (thingDef == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidDefName($"Building '{buildingDefName}' not found. Call actions_query_buildable_structures to see valid buildable defs.") with { TraceId = effectiveArgs.TraceId }));
                }

                // Only accept player-buildable, research-unlocked buildings.
                if (thingDef.category != ThingCategory.Building || !thingDef.BuildableByPlayer || !thingDef.IsResearchFinished)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidDefName($"'{thingDef.defName}' is not a player-buildable, research-unlocked building. Call actions_query_buildable_structures to see valid buildable defs.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 2. Auto-Stuff Selection (Stockpile-Aware)
                TryGetArgument<string>(argsRoot, "stuff_def", out var stuffDefName);
                ThingDef? selectedStuff = ConstructionIntentChainer.ResolveStuffForBuilding(thingDef, stuffDefName, map);

                // 3. Anchor and Placement Position
                Pawn? anchorPawn = actorResolution.Pawn;

                // Autonomy Permission & Resource Safety Guards
                var permCheck = ActionPermissionGuard.CheckPermission(anchorPawn, Id, AutonomyDomain.BlueprintPlacement, effectiveArgs);
                if (!permCheck.IsOk)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
                }

                var safetyCheck = ResourceSafetyGuard.CheckConstructionSafety(map, thingDef, selectedStuff);
                if (!safetyCheck.IsOk)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(safetyCheck.Error));
                }

                IntVec3 anchorPos = (anchorPawn != null && anchorPawn.Position.InHorDistOf(map.Center, 28f)) ? anchorPawn.Position : map.Center;
                TryGetArgument<string>(argsRoot, "location_hint", out var locationHint);
                locationHint = string.IsNullOrWhiteSpace(locationHint) ? "near_base" : locationHint.ToLowerInvariant().Trim();

                var rot = Rot4.North;
                string clusterReason = "default_fallback";
                int clusterRoomId = 0;
                IntVec3 targetCell = FindValidPlacementCell(
                    map,
                    thingDef,
                    anchorPos,
                    locationHint,
                    ref rot,
                    selectedStuff,
                    out clusterReason,
                    out clusterRoomId);
                if (!targetCell.IsValid && anchorPos != map.Center)
                {
                    targetCell = FindValidPlacementCell(
                        map,
                        thingDef,
                        map.Center,
                        locationHint,
                        ref rot,
                        selectedStuff,
                        out clusterReason,
                        out clusterRoomId);
                }
                if (!targetCell.IsValid)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal($"Unable to find unobstructed standable placement cell for '{thingDef.defName}' near {anchorPos}.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 4. Place Blueprint in RimWorld
                Blueprint_Build? blueprint = null;
                try
                {
                    blueprint = GenConstruct.PlaceBlueprintForBuild(thingDef, targetCell, map, rot, Faction.OfPlayer, selectedStuff);
                }
                catch (Exception ex)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal($"GenConstruct.PlaceBlueprintForBuild failed: {ex.Message}") with { TraceId = effectiveArgs.TraceId }));
                }

                if (blueprint == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal($"Blueprint for '{thingDef.defName}' could not be spawned at {targetCell}.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 5. Immediate Construction Chaining via Shared ConstructionIntentChainer
                bool autoConstruct = !TryGetArgument<bool>(argsRoot, "auto_construct", out var parsedAutoConstruct) || parsedAutoConstruct;

                bool sessionStarted = false;
                string builderName = "None";

                if (autoConstruct && anchorPawn != null)
                {
                    sessionStarted = ConstructionIntentChainer.TryChainSingleBlueprint(
                        anchorPawn,
                        map,
                        blueprint,
                        targetCell,
                        intentId: $"Construct_{thingDef.defName}",
                        durationTicks: 2500,
                        priority: 7.8f);
                    if (sessionStarted)
                    {
                        builderName = anchorPawn.Name?.ToStringShort ?? anchorPawn.Label;
                    }
                }

                string stuffLabel = selectedStuff?.label ?? ActionPermissionGuard.TranslateOrFallback(
                    "RimMind.Actions.Blueprint.DefaultMaterials",
                    "default materials");
                string placeMsg = ActionPermissionGuard.TranslateOrFallback(
                    "RimMind.Actions.Blueprint.PlaceSuccess",
                    "Successfully placed blueprint for {0} at ({1}, {2}) using {3}.",
                    thingDef.label,
                    targetCell.x,
                    targetCell.z,
                    stuffLabel);
                if (sessionStarted)
                {
                    placeMsg += ActionPermissionGuard.TranslateOrFallback(
                        "RimMind.Actions.Blueprint.IntentChained",
                        " Chained high-priority (7.8f) construction intent for {0}.",
                        builderName);
                }

                var successPayload = new
                {
                    status = "ok",
                    blueprint_id = blueprint.thingIDNumber,
                    building_def = thingDef.defName,
                    label = thingDef.label,
                    stuff_used = selectedStuff?.defName ?? "None",
                    placed_position = new { x = targetCell.x, z = targetCell.z },
                    rotation = rot.ToStringHuman(),
                    placement_reason = clusterReason,
                    room_id = clusterRoomId > 0 ? (int?)clusterRoomId : null,
                    chained_intent_session = sessionStarted,
                    assigned_builder = builderName,
                    message = placeMsg
                };

                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
                {
                    ToolCallId = effectiveArgs.ToolCallId,
                    ToolName = Id,
                    Content = JsonConvert.SerializeObject(successPayload, Formatting.Indented),
                    IsError = false
                }));
#endif
            }, ct);
        }

#if !RIMMIND_ACTIONS_TESTS
        private static IntVec3 FindValidPlacementCell(
            Map map,
            ThingDef def,
            IntVec3 anchor,
            string hint,
            ref Rot4 rot,
            ThingDef? stuff,
            out string clusterReason,
            out int clusterRoomId)
        {
            bool wantsIndoor = hint.Contains("indoor") || hint.Contains("bed") || hint.Contains("room");
            bool wantsOutdoor = hint.Contains("outdoor");
            Rot4[] rotations = def.rotatable
                ? new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West }
                : new[] { Rot4.North };

            // 1. Spatial topology + peer-furniture clustering solver using real footprint & interaction cell
            try
            {
                ColonySpatialSnapshot snapshot = MapSpatialSnapshotBuilder.Build(map, anchor.x, anchor.z, radius: 26);
                bool isBed = def.thingClass != null && typeof(Building_Bed).IsAssignableFrom(def.thingClass);
                bool isWorkTable = def.thingClass != null && typeof(Building_WorkTable).IsAssignableFrom(def.thingClass);
                bool isTable = def.surfaceType == SurfaceType.Eat;
                bool isJoy = def.building?.joyKind != null;
                bool isDefense = (def.building != null && def.building.IsTurret) || def.passability == Traversability.PassThroughOnly;
                SpatialBuildingCategory category = SpatialBuildingClassifier.Classify(
                    def.defName,
                    isBed: isBed,
                    isWorkTable: isWorkTable,
                    isTable: isTable,
                    isJoy: isJoy,
                    isDefense: isDefense);

                SpatialCandidateScore? bestOverall = null;
                Rot4 bestRot = rot;

                foreach (Rot4 candidateRot in rotations)
                {
                    CellRect occupied = GenAdj.OccupiedRect(IntVec3.Zero, candidateRot, def.Size);
                    IntVec3 interactionOffset = def.hasInteractionCell
                        ? def.interactionCellOffset.RotatedBy(candidateRot)
                        : IntVec3.Zero;

                    var req = new SpatialPlacementRequest
                    {
                        BuildingDefName = def.defName ?? string.Empty,
                        Category = category,
                        SizeX = Math.Max(1, occupied.Width),
                        SizeZ = Math.Max(1, occupied.Height),
                        FootprintMinOffsetX = occupied.minX,
                        FootprintMinOffsetZ = occupied.minZ,
                        HasInteractionCell = def.hasInteractionCell,
                        InteractionOffsetX = interactionOffset.x,
                        InteractionOffsetZ = interactionOffset.z,
                        LocationHint = hint,
                        AnchorX = anchor.x,
                        AnchorZ = anchor.z
                    };

                    List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(
                        snapshot,
                        req,
                        (cx, cz) => GenConstruct.CanPlaceBlueprintAt(def, new IntVec3(cx, 0, cz), candidateRot, map, false, null, null, stuff).Accepted);

                    if (ranked.Count > 0)
                    {
                        SpatialCandidateScore top = ranked[0];
                        if (bestOverall == null || top.Score > bestOverall.Score)
                        {
                            bestOverall = top;
                            bestRot = candidateRot;
                        }
                    }
                }

                if (bestOverall != null)
                {
                    rot = bestRot;
                    clusterReason = bestOverall.Reason;
                    clusterRoomId = bestOverall.RoomId;
                    return new IntVec3(bestOverall.X, 0, bestOverall.Z);
                }
            }
            catch
            {
                // Fall back to deterministic radial scan if spatial solver encounters an unexpected map state
            }

            // 2. Deterministic radial scan with room preference
            foreach (var cell in GenRadial.RadialCellsAround(anchor, 28, true))
            {
                if (!cell.IsValid || !cell.InBounds(map) || cell.Fogged(map) || !cell.Standable(map)) continue;
                var room = cell.GetRoom(map);
                if (wantsIndoor && (room == null || room.PsychologicallyOutdoors)) continue;
                if (wantsOutdoor && room != null && !room.PsychologicallyOutdoors) continue;
                if (room != null && room.IsDoorway) continue;

                foreach (var candidateRot in rotations)
                {
                    if (GenConstruct.CanPlaceBlueprintAt(def, cell, candidateRot, map, false, null, null, stuff).Accepted)
                    {
                        rot = candidateRot;
                        clusterReason = "radial_fallback";
                        clusterRoomId = room != null && !room.PsychologicallyOutdoors ? room.ID : 0;
                        return cell;
                    }
                }
            }

            // 3. Deterministic radial fallback without strict indoor/outdoor constraint
            foreach (var cell in GenRadial.RadialCellsAround(anchor, 28, true))
            {
                if (!cell.IsValid || !cell.InBounds(map) || cell.Fogged(map) || !cell.Standable(map)) continue;
                var room = cell.GetRoom(map);
                if (room != null && room.IsDoorway) continue;

                foreach (var candidateRot in rotations)
                {
                    if (GenConstruct.CanPlaceBlueprintAt(def, cell, candidateRot, map, false, null, null, stuff).Accepted)
                    {
                        rot = candidateRot;
                        clusterReason = "radial_unconstrained_fallback";
                        clusterRoomId = 0;
                        return cell;
                    }
                }
            }

            clusterReason = "none";
            clusterRoomId = 0;
            return IntVec3.Invalid;
        }
#endif
    }
}
