using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Actions.Policies;
using RimMind.Actions.Spatial;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimWorld;
#endif
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite query tool that captures a top-down annotated visual PNG map of the colony
    /// (with rooms, walls, doors, green growing zones, amber stockpiles, coordinate axes, legend,
    /// and numbered Set-of-Mark placement candidates [1]..[6]) alongside compact structured JSON
    /// and an ASCII fallback for non-vision models.
    /// </summary>
    public sealed class InspectMapViewCompositeTool : CompositeToolCallBase
    {
        public const int DefaultRadius = 16;
        public const int MinRadius = 6;
        public const int MaxRadius = 26;
        public const int MaxCandidates = 6;

        public override string Id => "actions_inspect_map_view";

        private static readonly string[] Aliases = new[]
        {
            "inspect_map_view",
            "actions.inspect_map_view"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "building_def",
            "radius"
        };

        protected override ToolKind ToolKind => ToolKind.Query;
        protected override AutonomyDomain? DefaultAutonomyDomain => null;
        protected override ToolScope? ToolScopes => ToolScope.ColonyBuild | ToolScope.ColonyManage;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => false;
        protected override string? DisplayName => "Inspect Map View";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "query",
            Description = "Captures an annotated top-down visual map image (PNG with legend, coordinates, rooms, green farms, amber stockpiles, and numbered Set-of-Mark candidate markers [1]..[6]) plus structured room/zone metadata and ASCII fallback. Supports zooming in via smaller radius (6-26) and panning via center_x/center_z before calling actions_place_blueprint.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"building_def\":{\"type\":\"string\",\"description\":\"Optional building DefName (e.g. 'Bed', 'FueledStove', 'PassiveCooler') to compute and highlight up to 6 legal non-farm placement candidates [1]..[6] cached for actions_place_blueprint(candidate_id=...).\"},\"center_x\":{\"type\":\"integer\",\"description\":\"Optional X map coordinate to center the view on (pair with center_z to pan/zoom to a specific room or area).\"},\"center_z\":{\"type\":\"integer\",\"description\":\"Optional Z map coordinate to center the view on (pair with center_x to pan/zoom to a specific room or area).\"},\"radius\":{\"type\":\"integer\",\"description\":\"Half-width of the map view in tiles (range 6-26, default 16). Use smaller values (e.g. 8-10) to zoom in on a room for high detail.\"},\"location_hint\":{\"type\":\"string\",\"description\":\"Optional placement preference when building_def is provided: 'near_base' (default), 'near_pawn', 'indoor', 'outdoor'.\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to resolve the map and anchor location.\"}},\"required\":[]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            return ExecuteDirectToolAsync(args, (argsRoot, effectiveArgs, _) =>
            {
                TryGetArgument<string>(argsRoot, "building_def", out var buildingDefName);
                buildingDefName = string.IsNullOrWhiteSpace(buildingDefName) ? null : buildingDefName.Trim();

                bool hasCenterX = TryGetArgument<int>(argsRoot, "center_x", out int requestedCenterX);
                bool hasCenterZ = TryGetArgument<int>(argsRoot, "center_z", out int requestedCenterZ);
                if (hasCenterX != hasCenterZ)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, "Parameters 'center_x' and 'center_z' must be provided together.") with { TraceId = effectiveArgs.TraceId }));
                }

                int radius = DefaultRadius;
                if (TryGetArgument<int>(argsRoot, "radius", out int parsedRadius) && parsedRadius > 0)
                {
                    radius = parsedRadius;
                }
                int clampedRadius = Math.Max(MinRadius, Math.Min(MaxRadius, radius));

                TryGetArgument<string>(argsRoot, "location_hint", out var locationHint);
                locationHint = string.IsNullOrWhiteSpace(locationHint) ? "near_base" : locationHint.Trim().ToLowerInvariant();

#if RIMMIND_ACTIONS_TESTS
                int testCenterX = hasCenterX ? requestedCenterX : 12;
                int testCenterZ = hasCenterZ ? requestedCenterZ : 12;
                ColonySpatialSnapshot testSnapshot = BuildSyntheticTestSnapshot(testCenterX, testCenterZ, clampedRadius);
                var testCandidates = new List<SpatialVisualCandidate>();

                if (!string.IsNullOrWhiteSpace(buildingDefName))
                {
                    SpatialBuildingCategory testCat = SpatialBuildingClassifier.Classify(buildingDefName);
                    var testReq = new SpatialPlacementRequest
                    {
                        BuildingDefName = buildingDefName!,
                        Category = testCat,
                        SizeX = 1,
                        SizeZ = 1,
                        LocationHint = locationHint,
                        AnchorX = testCenterX,
                        AnchorZ = testCenterZ,
                        AllowGrowingZone = false
                    };
                    List<SpatialCandidateScore> testRanked = ColonySpatialClusterSolver.RankCandidates(testSnapshot, testReq);
                    var rawVisuals = new List<SpatialVisualCandidate>(testRanked.Count);
                    for (int i = 0; i < testRanked.Count; i++)
                    {
                        SpatialCandidateScore s = testRanked[i];
                        rawVisuals.Add(new SpatialVisualCandidate
                        {
                            X = s.X,
                            Z = s.Z,
                            RotationByte = 0,
                            Rotation = "North",
                            SizeX = 1,
                            SizeZ = 1,
                            RoomId = s.RoomId,
                            Reason = s.Reason,
                            Score = s.Score
                        });
                    }

                    testCandidates = SelectDiverseCandidates(rawVisuals, MaxCandidates, minManhattanSpacing: 3);
                    VisualCandidateCache.Store(mapId: 1, buildingDefName!, testCandidates, currentTick: PlaceBlueprintCompositeTool.TestCurrentTick);
                }

                ToolResult testResult = BuildVisualToolResult(
                    effectiveArgs.ToolCallId,
                    testSnapshot,
                    testCenterX,
                    testCenterZ,
                    clampedRadius,
                    buildingDefName,
                    testCandidates);
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(testResult));
#else
                var actorResolution = ActionPermissionGuard.ResolveActorAndMap(
                    effectiveArgs,
                    "pawn_id",
                    fallbackToAnyActiveColonist: true,
                    parsedArgs: argsRoot);

                Map? map = actorResolution.Map ?? Find.CurrentMap;
                if (map == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal("No active map available for visual map inspection.") with { TraceId = effectiveArgs.TraceId }));
                }

                Pawn? anchorPawn = actorResolution.Pawn;
                bool useExplicitCenter = hasCenterX && hasCenterZ;
                IntVec3 fallbackAnchor = (anchorPawn != null && anchorPawn.Position.InHorDistOf(map.Center, 36f))
                    ? anchorPawn.Position
                    : map.Center;

                int queryX = useExplicitCenter
                    ? Math.Max(0, Math.Min(map.Size.x - 1, requestedCenterX))
                    : fallbackAnchor.x;
                int queryZ = useExplicitCenter
                    ? Math.Max(0, Math.Min(map.Size.z - 1, requestedCenterZ))
                    : fallbackAnchor.z;

                ColonySpatialSnapshot snapshot = MapSpatialSnapshotBuilder.Build(
                    map,
                    queryX,
                    queryZ,
                    clampedRadius,
                    useExplicitCenter);

                int viewCenterX = useExplicitCenter ? queryX : snapshot.ColonyAnchorX;
                int viewCenterZ = useExplicitCenter ? queryZ : snapshot.ColonyAnchorZ;

                var selectedCandidates = new List<SpatialVisualCandidate>();
                string? resolvedBuildingDefName = null;

                if (!string.IsNullOrWhiteSpace(buildingDefName))
                {
                    ThingDef? thingDef = ConstructionIntentChainer.ResolveThingDef(buildingDefName!);
                    if (thingDef == null)
                    {
                        return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                            RimMindErrors.InvalidDefName($"Building '{buildingDefName}' not found. Call actions_query_buildable_structures to see valid buildable defs.") with { TraceId = effectiveArgs.TraceId }));
                    }

                    if (thingDef.category != ThingCategory.Building || !thingDef.BuildableByPlayer || !thingDef.IsResearchFinished)
                    {
                        return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                            RimMindErrors.InvalidDefName($"'{thingDef.defName}' is not a player-buildable, research-unlocked building. Call actions_query_buildable_structures to see valid buildable defs.") with { TraceId = effectiveArgs.TraceId }));
                    }

                    resolvedBuildingDefName = thingDef.defName;
                    ThingDef? selectedStuff = ConstructionIntentChainer.ResolveStuffForBuilding(thingDef, null, map);
                    SpatialBuildingCategory category = PlaceBlueprintCompositeTool.ClassifyThingDef(thingDef);
                    Rot4[] rotations = thingDef.rotatable
                        ? new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West }
                        : new[] { Rot4.North };

                    // Keep best rotation per (X, Z) coordinate across allowed rotations
                    var bestByCell = new Dictionary<long, SpatialVisualCandidate>();

                    foreach (Rot4 candidateRot in rotations)
                    {
                        CellRect occupied = GenAdj.OccupiedRect(IntVec3.Zero, candidateRot, thingDef.Size);
                        IntVec3 interactionOffset = thingDef.hasInteractionCell
                            ? thingDef.interactionCellOffset.RotatedBy(candidateRot)
                            : IntVec3.Zero;

                        var req = new SpatialPlacementRequest
                        {
                            BuildingDefName = thingDef.defName ?? string.Empty,
                            Category = category,
                            SizeX = Math.Max(1, occupied.Width),
                            SizeZ = Math.Max(1, occupied.Height),
                            FootprintMinOffsetX = occupied.minX,
                            FootprintMinOffsetZ = occupied.minZ,
                            HasInteractionCell = thingDef.hasInteractionCell,
                            InteractionOffsetX = interactionOffset.x,
                            InteractionOffsetZ = interactionOffset.z,
                            LocationHint = locationHint,
                            AnchorX = fallbackAnchor.x,
                            AnchorZ = fallbackAnchor.z,
                            TargetX = useExplicitCenter ? (int?)viewCenterX : null,
                            TargetZ = useExplicitCenter ? (int?)viewCenterZ : null,
                            AllowGrowingZone = false
                        };

                        List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(
                            snapshot,
                            req,
                            (cx, cz) =>
                            {
                                IntVec3 cell = new IntVec3(cx, 0, cz);
                                return !PlaceBlueprintCompositeTool.HasGrowingZoneOverlap(map, thingDef, cell, candidateRot)
                                    && GenConstruct.CanPlaceBlueprintAt(thingDef, cell, candidateRot, map, false, null, null, selectedStuff).Accepted;
                            });

                        int scanCap = Math.Min(ranked.Count, 80);
                        for (int i = 0; i < scanCap; i++)
                        {
                            SpatialCandidateScore s = ranked[i];
                            long cellKey = PackCoord(s.X, s.Z);
                            if (!bestByCell.TryGetValue(cellKey, out SpatialVisualCandidate? existing) || s.Score > existing.Score)
                            {
                                bestByCell[cellKey] = new SpatialVisualCandidate
                                {
                                    X = s.X,
                                    Z = s.Z,
                                    RotationByte = candidateRot.AsByte,
                                    Rotation = candidateRot.ToStringHuman(),
                                    SizeX = req.SizeX,
                                    SizeZ = req.SizeZ,
                                    FootprintMinOffsetX = req.FootprintMinOffsetX,
                                    FootprintMinOffsetZ = req.FootprintMinOffsetZ,
                                    RoomId = s.RoomId,
                                    Reason = s.Reason,
                                    Score = s.Score
                                };
                            }
                        }
                    }

                    var merged = bestByCell.Values
                        .OrderByDescending(c => c.Score)
                        .ThenBy(c => Math.Abs(c.X - viewCenterX) + Math.Abs(c.Z - viewCenterZ))
                        .ThenBy(c => c.X)
                        .ThenBy(c => c.Z)
                        .ToList();

                    selectedCandidates = SelectDiverseCandidates(merged, MaxCandidates, minManhattanSpacing: 3);
                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    VisualCandidateCache.Store(map.uniqueID, thingDef.defName, selectedCandidates, currentTick);
                }

                ToolResult liveResult = BuildVisualToolResult(
                    effectiveArgs.ToolCallId,
                    snapshot,
                    viewCenterX,
                    viewCenterZ,
                    clampedRadius,
                    resolvedBuildingDefName,
                    selectedCandidates);
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(liveResult));
#endif
            }, ct);
        }

        /// <summary>
        /// Picks up to <paramref name="maxCount"/> spatially diverse candidates from a score-sorted list,
        /// preferring candidates separated by at least <paramref name="minManhattanSpacing"/> tiles before
        /// filling remaining slots with the highest-scoring remaining distinct cells.
        /// </summary>
        internal static List<SpatialVisualCandidate> SelectDiverseCandidates(
            IReadOnlyList<SpatialVisualCandidate> ranked,
            int maxCount = MaxCandidates,
            int minManhattanSpacing = 3)
        {
            var selected = new List<SpatialVisualCandidate>();
            if (ranked == null || ranked.Count == 0 || maxCount <= 0)
                return selected;

            var usedCoords = new HashSet<long>();

            // Pass 1: spatially well-separated candidates
            for (int i = 0; i < ranked.Count && selected.Count < maxCount; i++)
            {
                SpatialVisualCandidate cand = ranked[i];
                if (cand == null) continue;
                long coordKey = PackCoord(cand.X, cand.Z);
                if (usedCoords.Contains(coordKey)) continue;

                bool tooClose = false;
                for (int j = 0; j < selected.Count; j++)
                {
                    int dist = Math.Abs(selected[j].X - cand.X) + Math.Abs(selected[j].Z - cand.Z);
                    if (dist < minManhattanSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    usedCoords.Add(coordKey);
                    selected.Add(CloneWithId(cand, selected.Count + 1));
                }
            }

            // Pass 2: backfill if fewer than maxCount spatially separated candidates existed
            for (int i = 0; i < ranked.Count && selected.Count < maxCount; i++)
            {
                SpatialVisualCandidate cand = ranked[i];
                if (cand == null) continue;
                long coordKey = PackCoord(cand.X, cand.Z);
                if (usedCoords.Contains(coordKey)) continue;

                usedCoords.Add(coordKey);
                selected.Add(CloneWithId(cand, selected.Count + 1));
            }

            return selected;
        }

        private ToolResult BuildVisualToolResult(
            string? toolCallId,
            ColonySpatialSnapshot snapshot,
            int viewCenterX,
            int viewCenterZ,
            int radius,
            string? buildingDefName,
            IReadOnlyList<SpatialVisualCandidate> candidates)
        {
            SpatialVisualRenderResult rendered = SpatialVisualMapRenderer.Render(
                snapshot,
                viewCenterX,
                viewCenterZ,
                radius,
                candidates);

            string asciiFallback = SpatialAsciiRenderer.RenderCompactAsciiGrid(
                snapshot,
                viewCenterX,
                viewCenterZ,
                Math.Min(12, radius));

            int growingCells = 0;
            int stockpileCells = 0;
            for (int x = rendered.GridMinX; x <= rendered.GridMaxX; x++)
            {
                for (int z = rendered.GridMinZ; z <= rendered.GridMaxZ; z++)
                {
                    if (!snapshot.InBounds(x, z)) continue;
                    if (snapshot.IsGrowingZone(x, z)) growingCells++;
                    if (snapshot.IsStorageZone(x, z)) stockpileCells++;
                }
            }

            var roomsPayload = snapshot.Rooms
                .Where(r => r.IsEnclosed
                    && r.CenterX >= rendered.GridMinX && r.CenterX <= rendered.GridMaxX
                    && r.CenterZ >= rendered.GridMinZ && r.CenterZ <= rendered.GridMaxZ)
                .OrderByDescending(r => r.CellCount)
                .Take(12)
                .Select(r => new
                {
                    room_id = r.RoomId,
                    role = r.Role,
                    cells = r.CellCount,
                    beds = r.BedCount,
                    worktables = r.WorktableCount,
                    tables = r.TableCount,
                    center_x = r.CenterX,
                    center_z = r.CenterZ
                })
                .ToList();

            var candidatesPayload = candidates.Select(c => new
            {
                candidate_id = c.CandidateId,
                x = c.X,
                z = c.Z,
                rotation = c.Rotation,
                room_id = c.RoomId > 0 ? (int?)c.RoomId : null,
                reason = c.Reason,
                score = Math.Round(c.Score, 1)
            }).ToList();

            string nextHint = candidates.Count > 0 && !string.IsNullOrWhiteSpace(buildingDefName)
                ? $"Inspect the attached PNG image (green striped=Farm/NoBuild, amber=Stockpile, magenta [1]..[{candidates.Count}]=legal candidates). Call actions_place_blueprint(building_def='{buildingDefName}', candidate_id=<1..{candidates.Count}>) or pass exact target_x/target_z. Zoom in with a smaller radius (6-10) if needed."
                : "Inspect the attached PNG image (green striped=Farm/NoBuild, amber=Stockpile). Re-call actions_inspect_map_view with building_def to highlight numbered candidates [1]..[6], or zoom in with a smaller radius (6-10) and center_x/center_z.";

            var payload = new
            {
                status = "ok",
                view = new
                {
                    center_x = viewCenterX,
                    center_z = viewCenterZ,
                    radius = rendered.Radius,
                    min_x = rendered.GridMinX,
                    max_x = rendered.GridMaxX,
                    min_z = rendered.GridMinZ,
                    max_z = rendered.GridMaxZ,
                    image_width = rendered.Width,
                    image_height = rendered.Height,
                    tile_pixels = rendered.TilePixels
                },
                building_def = buildingDefName,
                rooms = roomsPayload,
                zones_summary = new
                {
                    growing_cells = growingCells,
                    stockpile_cells = stockpileCells
                },
                candidates = candidatesPayload,
                ascii_fallback = asciiFallback,
                next_step_hint = nextHint
            };

            var attachment = new LlmImageAttachment
            {
                MediaType = "image/png",
                Base64Data = rendered.Base64Png,
                Label = $"map_view_r{rendered.Radius}_{viewCenterX}_{viewCenterZ}",
                Detail = "high"
            };

            return new ToolResult
            {
                ToolCallId = toolCallId,
                ToolName = Id,
                Content = JsonConvert.SerializeObject(payload, Formatting.Indented),
                IsError = false,
                Attachments = new[] { attachment }
            };
        }

        private static SpatialVisualCandidate CloneWithId(SpatialVisualCandidate source, int candidateId)
        {
            return new SpatialVisualCandidate
            {
                CandidateId = candidateId,
                X = source.X,
                Z = source.Z,
                RotationByte = source.RotationByte,
                Rotation = source.Rotation,
                SizeX = source.SizeX,
                SizeZ = source.SizeZ,
                FootprintMinOffsetX = source.FootprintMinOffsetX,
                FootprintMinOffsetZ = source.FootprintMinOffsetZ,
                RoomId = source.RoomId,
                Reason = source.Reason,
                Score = source.Score
            };
        }

        private static long PackCoord(int x, int z) => (((long)x) << 32) | (uint)z;

#if RIMMIND_ACTIONS_TESTS
        private static ColonySpatialSnapshot BuildSyntheticTestSnapshot(int centerX, int centerZ, int radius)
        {
            int minX = centerX - radius;
            int minZ = centerZ - radius;
            int dim = radius * 2 + 1;
            var snapshot = new ColonySpatialSnapshot(minX, minZ, dim, dim)
            {
                ColonyAnchorX = centerX,
                ColonyAnchorZ = centerZ,
                HasBaseAnchor = true
            };

            for (int x = snapshot.MinX; x <= snapshot.MaxX; x++)
            {
                for (int z = snapshot.MinZ; z <= snapshot.MaxZ; z++)
                {
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false);
                }
            }

            // Synthetic enclosed bedroom near center
            int rMinX = centerX - 3;
            int rMaxX = centerX + 3;
            int rMinZ = centerZ - 2;
            int rMaxZ = centerZ + 3;
            snapshot.AddRoom(new SpatialRoomSummary
            {
                RoomId = 1,
                Role = "Bedroom",
                CellCount = 20,
                BedCount = 1,
                CenterX = centerX,
                CenterZ = centerZ + 1,
                IsEnclosed = true
            });
            for (int x = rMinX; x <= rMaxX; x++)
            {
                for (int z = rMinZ; z <= rMaxZ; z++)
                {
                    if (!snapshot.InBounds(x, z)) continue;
                    bool perimeter = x == rMinX || x == rMaxX || z == rMinZ || z == rMaxZ;
                    if (x == centerX && z == rMinZ)
                        snapshot.SetCell(x, z, SpatialCellKind.Door, roomId: 0, roofed: true);
                    else if (perimeter)
                        snapshot.SetCell(x, z, SpatialCellKind.Wall, roomId: 0, roofed: true);
                    else
                        snapshot.SetCell(x, z, SpatialCellKind.IndoorRoom, roomId: 1, roofed: true);
                }
            }

            if (snapshot.InBounds(centerX + 2, centerZ + 2))
            {
                snapshot.SetCell(centerX + 2, centerZ + 2, SpatialCellKind.OccupiedFurniture, roomId: 1, roofed: true);
                snapshot.Peers.Add(new SpatialPeerItem
                {
                    DefName = "Bed",
                    Category = SpatialBuildingCategory.Sleeping,
                    X = centerX + 2,
                    Z = centerZ + 2,
                    RoomId = 1,
                    IsBlueprint = false
                });
            }

            // Synthetic Zone_Growing farm patch south-west of bedroom
            for (int x = centerX - 5; x <= centerX - 2; x++)
            {
                for (int z = centerZ - 5; z <= centerZ - 3; z++)
                {
                    if (snapshot.InBounds(x, z))
                        snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false, storageZone: false, growingZone: true);
                }
            }

            // Synthetic Zone_Stockpile patch south-east of bedroom
            for (int x = centerX + 2; x <= centerX + 5; x++)
            {
                for (int z = centerZ - 5; z <= centerZ - 3; z++)
                {
                    if (snapshot.InBounds(x, z))
                        snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false, storageZone: true, growingZone: false);
                }
            }

            return snapshot;
        }
#endif
    }
}
