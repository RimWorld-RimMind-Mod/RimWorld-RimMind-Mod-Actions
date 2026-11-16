using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
#if !RIMMIND_ACTIONS_TESTS
using RimMind.Actions.Spatial;
using RimWorld;
using Verse.AI;
#endif
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite action tool that places a building blueprint with smart obstacle clearance,
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
        {
            string? buildingDefName = root?["building_def"]?.ToString();
            if (string.IsNullOrWhiteSpace(buildingDefName) && args != null)
            {
                TryGetArgument<string>(args.ArgumentsJson, "building_def", out buildingDefName);
            }
            return AutonomyDomain.BlueprintPlacement;
        }

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            if (!TryGetArgument<string>(args.ArgumentsJson, "building_def", out var buildingDefName)
                || string.IsNullOrWhiteSpace(buildingDefName))
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, "Missing required parameter 'building_def'.")));
            }

#if RIMMIND_ACTIONS_TESTS
            var domain = ResolveAutonomyDomain(TryParseArgumentsObject(args.ArgumentsJson), args)
                ?? AutonomyDomain.BlueprintPlacement;
            if (TryGetArgument<int>(args.ArgumentsJson, "pawn_id", out int testPawnId) && testPawnId > 0)
            {
                var guard = GuardColonistTarget(testPawnId, domain, args);
                if (guard.IsErr)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(guard.Error));
                }
            }
            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok($"Placed {buildingDefName}", args.ToolCallId, Id)));
#else
            var actorResolution = ActionPermissionGuard.ResolveActorAndMap(args, "pawn_id", fallbackToAnyActiveColonist: true);
            if (actorResolution.Error != null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, actorResolution.Error)));
            }

            var map = actorResolution.Map;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for blueprint placement.")));
            }

            // 1. Resolve BuildableDef
            var trimmedBuildingName = buildingDefName.Trim();
            var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(trimmedBuildingName);
            if (thingDef == null)
            {
                // Fallback: null-safe case-insensitive search
                thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(d => d != null
                                      && (string.Equals(d.defName, trimmedBuildingName, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(d.label, trimmedBuildingName, StringComparison.OrdinalIgnoreCase)));
            }

            if (thingDef == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidDefName($"Building '{buildingDefName}' not found. Call actions_query_buildable_structures to see valid buildable defs.")));
            }

            // The label fallback above searches every ThingDef (items, plants...): only accept player-buildable,
            // research-unlocked buildings.
            if (thingDef.category != ThingCategory.Building || !thingDef.BuildableByPlayer || !thingDef.IsResearchFinished)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidDefName($"'{thingDef.defName}' is not a player-buildable, research-unlocked building. Call actions_query_buildable_structures to see valid buildable defs.")));
            }

            // 2. Auto-Stuff Selection (Stockpile-Aware)
            ThingDef? selectedStuff = null;
            TryGetArgument<string>(args.ArgumentsJson, "stuff_def", out var stuffDefName);
            if (!string.IsNullOrWhiteSpace(stuffDefName))
            {
                var trimmedStuffName = stuffDefName.Trim();
                selectedStuff = DefDatabase<ThingDef>.GetNamedSilentFail(trimmedStuffName)
                    ?? DefDatabase<ThingDef>.AllDefsListForReading
                        .FirstOrDefault(d => d != null
                                          && (string.Equals(d.defName, trimmedStuffName, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(d.label, trimmedStuffName, StringComparison.OrdinalIgnoreCase)));
                // Discard LLM-supplied stuff that this building cannot be made from; auto-selection below takes over.
                if (selectedStuff != null && (!thingDef.MadeFromStuff || !GenStuff.AllowedStuffsFor(thingDef).Contains(selectedStuff)))
                {
                    selectedStuff = null;
                }
            }

            if (thingDef.MadeFromStuff)
            {
                if (selectedStuff == null)
                {
                    // Scan valid stuffs for this building and pick the one with highest available count on map
                    var validStuffs = GenStuff.AllowedStuffsFor(thingDef).ToList();
                    selectedStuff = validStuffs
                        .OrderByDescending(s => ResourceSafetyGuard.GetAvailableCount(map, s))
                        .FirstOrDefault();

                    if (selectedStuff == null || ResourceSafetyGuard.GetAvailableCount(map, selectedStuff) <= 0)
                    {
                        selectedStuff = GenStuff.DefaultStuffFor(thingDef) ?? ThingDefOf.WoodLog ?? ThingDefOf.Steel;
                    }
                }
            }
            else
            {
                selectedStuff = null;
            }

            // 3. Anchor and Placement Position
            Pawn? anchorPawn = actorResolution.Pawn;

            // Autonomy Permission & Resource Safety Guards
            var autonomyDomain = AutonomyDomain.BlueprintPlacement;
            var permCheck = ActionPermissionGuard.CheckPermission(anchorPawn, Id, autonomyDomain, args);
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
            TryGetArgument<string>(args.ArgumentsJson, "location_hint", out var locationHint);
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
                    RimMindErrors.Internal($"Unable to find unobstructed standable placement cell for '{thingDef.defName}' near {anchorPos}.")));
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
                    RimMindErrors.Internal($"GenConstruct.PlaceBlueprintForBuild failed: {ex.Message}")));
            }

            if (blueprint == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal($"Blueprint for '{thingDef.defName}' could not be spawned at {targetCell}.")));
            }

            // 5. Immediate Construction Chaining via Intent Lease (Shortcoming 2 Solution)
            TryGetArgument<bool>(args.ArgumentsJson, "auto_construct", out var autoConstruct);
            // Default to true if not explicitly set to false
            if (args.ArgumentsJson == null || !args.ArgumentsJson.Contains("auto_construct"))
            {
                autoConstruct = true;
            }

            bool sessionStarted = false;
            string builderName = "None";

            if (autoConstruct && anchorPawn != null)
            {
                int startTick = Find.TickManager?.TicksGame ?? 0;
                sessionStarted = RimMindAPI.Agent.StartIntentSession(
                    pawn: anchorPawn,
                    intentId: $"Construct_{thingDef.defName}",
                    startTick: startTick,
                    durationTicks: 2500, // 1 in-game hour lease
                    priority: 7.8f,
                    target: blueprint,
                    jobProvider: p => IssueConstructionSubJob(p, map, blueprint, targetCell),
                    completionPredicate: p =>
                        map == null
                        || ((blueprint == null || blueprint.Destroyed || !blueprint.Spawned)
                            && !targetCell.GetThingList(map).OfType<Frame>().Any(f => !f.Destroyed && f.Spawned)));
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
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = JsonConvert.SerializeObject(successPayload, Formatting.Indented),
                IsError = false
            }));
#endif
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

            // 1. Spatial topology + peer-furniture clustering solver (replaces random spawn-adjacent placement)
            try
            {
                ColonySpatialSnapshot snapshot = MapSpatialSnapshotBuilder.Build(map, anchor.x, anchor.z, radius: 26);
                bool isBed = def.thingClass != null && typeof(Building_Bed).IsAssignableFrom(def.thingClass);
                bool isWorkTable = def.thingClass != null && typeof(Building_WorkTable).IsAssignableFrom(def.thingClass);
                bool isTable = def.surfaceType == SurfaceType.Eat;
                bool isDefense = (def.building != null && def.building.IsTurret) || def.passability == Traversability.PassThroughOnly;
                SpatialBuildingCategory category = ColonySpatialClusterSolver.ClassifyBuilding(
                    def.defName,
                    isBed: isBed,
                    isWorkTable: isWorkTable,
                    isTable: isTable,
                    isDefense: isDefense);

                SpatialCandidateScore? bestOverall = null;
                Rot4 bestRot = rot;

                foreach (Rot4 candidateRot in rotations)
                {
                    var req = new SpatialPlacementRequest
                    {
                        BuildingDefName = def.defName ?? string.Empty,
                        Category = category,
                        SizeX = 1,
                        SizeZ = 1,
                        HasInteractionCell = false,
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

        private static Job? IssueConstructionSubJob(Pawn pawn, Map map, Blueprint_Build blueprint, IntVec3 targetCell)
        {
            if (pawn == null || map == null || !targetCell.IsValid || !targetCell.InBounds(map)) return null;

            // Respect the player's work tab: never rewrite priorities. Pawns that cannot or are not assigned to
            // construct are skipped; vanilla construction workers will still pick up the placed blueprint.
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                || (pawn.workSettings != null && !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction)))
            {
                return null;
            }

            try
            {
                // Step 1: If blueprint is still active, try delivering resources then finishing frame
                if (blueprint != null && !blueprint.Destroyed && blueprint.Spawned)
                {
                    if (pawn.CanReserveAndReach(blueprint, PathEndMode.Touch, Danger.Some))
                    {
                        var deliverBpGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints")?.Worker as WorkGiver_Scanner
                            ?? new WorkGiver_ConstructDeliverResourcesToBlueprints();
                        var haulJob = deliverBpGiver.JobOnThing(pawn, blueprint, true);
                        if (haulJob != null) return haulJob;

                        var finishBpGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames")?.Worker as WorkGiver_Scanner
                            ?? new WorkGiver_ConstructFinishFrames();
                        var finishBpJob = finishBpGiver.JobOnThing(pawn, blueprint, true);
                        if (finishBpJob != null) return finishBpJob;
                    }
                    return null;
                }

                // Step 2: Blueprint was replaced by a Frame at targetCell
                Frame? frame = targetCell.GetThingList(map).OfType<Frame>().FirstOrDefault(f => !f.Destroyed && f.Spawned);
                if (frame != null && pawn.CanReserveAndReach(frame, PathEndMode.Touch, Danger.Some))
                {
                    var deliverFrameGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToFrames")?.Worker as WorkGiver_Scanner
                        ?? new WorkGiver_ConstructDeliverResourcesToFrames();
                    var deliverJob = deliverFrameGiver.JobOnThing(pawn, frame, true);
                    if (deliverJob != null) return deliverJob;

                    var finishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames")?.Worker as WorkGiver_Scanner
                        ?? new WorkGiver_ConstructFinishFrames();
                    var finishJob = finishGiver.JobOnThing(pawn, frame, true);
                    if (finishJob != null) return finishJob;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
#endif

        private static bool IsEssentialSurvivalStructure(string? defName)
        {
            if (string.IsNullOrWhiteSpace(defName)) return false;
            return defName!.Equals("Bed", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Bedroll", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("DoubleBed", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("RoyalBed", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("HospitalBed", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("SleepingSpot", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("DoubleSleepingSpot", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Campfire", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("TorchLamp", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("PassiveCooler", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Heater", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Wall", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Door", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Autodoor", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Sandbags", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Barricade", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("TableShort", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Table1x2c", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Table2x2c", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Table2x4c", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("DiningChair", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Stool", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("Armchair", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("HoopstoneRing", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("HorseshoesPin", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("ChessTable", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("FueledStove", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("ElectricStove", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("ButcherSpot", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("TableButcher", StringComparison.OrdinalIgnoreCase)
                || defName.Equals("SimpleResearchBench", StringComparison.OrdinalIgnoreCase);
        }
    }
}
