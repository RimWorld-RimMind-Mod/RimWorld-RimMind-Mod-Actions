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
using RimMind.Infrastructure.Verse;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite action tool that places a building blueprint with smart obstacle clearance,
    /// automatic stockpile material detection, and immediate colonist construction intent chaining.
    /// </summary>
    public sealed class PlaceBlueprintCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_place_blueprint";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Places a building blueprint on the map with smart cell placement, material auto-detection, and immediate colonist construction chaining.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"building_def\":{\"type\":\"string\",\"description\":\"DefName of the building to place (e.g. 'Bed', 'PassiveCooler', 'FueledStove', 'Campfire', 'TableShort'). Call actions_query_buildable_structures to inspect valid buildables.\"},\"stuff_def\":{\"type\":\"string\",\"description\":\"Optional material (e.g. 'WoodLog', 'Steel'). If omitted, automatically selects the most abundant valid material in the colony's stockpiles.\"},\"location_hint\":{\"type\":\"string\",\"description\":\"Optional placement preference: 'near_base' (default), 'near_pawn', 'indoor', 'outdoor'.\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to perform construction and anchor location.\"},\"auto_construct\":{\"type\":\"boolean\",\"description\":\"Whether to chain an immediate Intent Session on the colonist to construct the placed blueprint (default: true).\"}},\"required\":[\"building_def\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

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

            var map = Find.CurrentMap;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for blueprint placement.")));
            }

            // 1. Resolve BuildableDef
            var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(buildingDefName.Trim());
            if (thingDef == null)
            {
                // Fallback: case-insensitive search
                thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.defName.Equals(buildingDefName.Trim(), StringComparison.OrdinalIgnoreCase)
                                      || d.label.Equals(buildingDefName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (thingDef == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidDefName($"Building '{buildingDefName}' not found. Call actions_query_buildable_structures to see valid buildable defs.")));
            }

            // 2. Auto-Stuff Selection (Stockpile-Aware)
            ThingDef? selectedStuff = null;
            TryGetArgument<string>(args.ArgumentsJson, "stuff_def", out var stuffDefName);
            if (!string.IsNullOrWhiteSpace(stuffDefName))
            {
                selectedStuff = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName.Trim());
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
            Pawn? anchorPawn = ActionPermissionGuard.ResolveColonistOnMap(map, args, "pawn_id", fallbackToAnyActiveColonist: true);

            // Autonomy Permission & Resource Safety Guards
            var autonomyDomain = IsEssentialSurvivalStructure(thingDef.defName)
                ? AutonomyDomain.EmergencySurvival
                : AutonomyDomain.BlueprintPlacement;
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

            IntVec3 anchorPos = anchorPawn?.Position ?? map.Center;
            TryGetArgument<string>(args.ArgumentsJson, "location_hint", out var locationHint);
            locationHint = string.IsNullOrWhiteSpace(locationHint) ? "near_base" : locationHint.ToLowerInvariant().Trim();

            var rot = Rot4.North;
            IntVec3 targetCell = FindValidPlacementCell(map, thingDef, anchorPos, locationHint, rot, selectedStuff);
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
                var comp = anchorPawn.GetComp<CompPawnAgent>();
                if (comp != null)
                {
                    int startTick = Find.TickManager?.TicksGame ?? 0;
                    var session = new AgentIntentSession(
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

                    comp.StartIntentSession(session);
                    sessionStarted = true;
                    builderName = anchorPawn.Name?.ToStringShort ?? anchorPawn.Label;
                }
            }

            var successPayload = new
            {
                status = "ok",
                blueprint_id = blueprint.thingIDNumber,
                building_def = thingDef.defName,
                label = thingDef.label,
                stuff_used = selectedStuff?.defName ?? "None",
                placed_position = new { x = targetCell.x, z = targetCell.z },
                chained_intent_session = sessionStarted,
                assigned_builder = builderName,
                message = $"Successfully placed blueprint for {thingDef.label} at ({targetCell.x}, {targetCell.z}) using {selectedStuff?.label ?? "default materials"}." +
                          (sessionStarted ? $" Chained high-priority (7.8f) construction intent for {builderName}." : "")
            };

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = JsonConvert.SerializeObject(successPayload, Formatting.Indented),
                IsError = false
            }));
        }

        private static IntVec3 FindValidPlacementCell(Map map, ThingDef def, IntVec3 anchor, string hint, Rot4 rot, ThingDef? stuff)
        {
            bool wantsIndoor = hint.Contains("indoor") || hint.Contains("bed") || hint.Contains("room");
            bool wantsOutdoor = hint.Contains("outdoor");

            for (int radius = 2; radius <= 25; radius += 2)
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    var cell = CellFinder.StandableCellNear(anchor, map, radius);
                    if (!cell.IsValid || !cell.InBounds(map) || cell.Fogged(map)) continue;

                    var room = cell.GetRoom(map);
                    if (wantsIndoor && (room == null || room.PsychologicallyOutdoors)) continue;
                    if (wantsOutdoor && room != null && !room.PsychologicallyOutdoors) continue;

                    // Ensure cell does not block doors
                    if (room != null && room.IsDoorway) continue;

                    // Validate building placement rules
                    var acceptance = GenConstruct.CanPlaceBlueprintAt(def, cell, rot, map, false, null, null, stuff);
                    if (acceptance.Accepted)
                    {
                        return cell;
                    }
                }
            }

            // Fallback: general standable search with CanPlaceBlueprintAt validation
            for (int r = 4; r <= 20; r += 4)
            {
                for (int i = 0; i < 8; i++)
                {
                    var fallbackCell = CellFinder.StandableCellNear(anchor, map, r);
                    if (!fallbackCell.IsValid || !fallbackCell.InBounds(map) || fallbackCell.Fogged(map)) continue;
                    if (GenConstruct.CanPlaceBlueprintAt(def, fallbackCell, rot, map, false, null, null, stuff).Accepted)
                    {
                        return fallbackCell;
                    }
                }
            }

            return IntVec3.Invalid;
        }

        private static Job? IssueConstructionSubJob(Pawn pawn, Map map, Blueprint_Build blueprint, IntVec3 targetCell)
        {
            if (pawn == null || map == null || !targetCell.IsValid || !targetCell.InBounds(map)) return null;

            if (pawn.workSettings != null && !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction))
            {
                pawn.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
            }

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

            return null;
        }

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
