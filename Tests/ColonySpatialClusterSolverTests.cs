using System.Collections.Generic;
using RimMind.Actions.Spatial;
using Xunit;

namespace RimMind.Actions.Tests
{
    public sealed class ColonySpatialClusterSolverTests
    {
        private static ColonySpatialSnapshot BuildBedroomAndWorkshopSnapshot()
        {
            var snapshot = new ColonySpatialSnapshot(0, 0, 24, 24);
            snapshot.ColonyAnchorX = 12;
            snapshot.ColonyAnchorZ = 12;
            snapshot.HasBaseAnchor = true;

            for (int x = 0; x <= 23; x++)
            {
                for (int z = 0; z <= 23; z++)
                {
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false);
                }
            }

            // Room 1 (Bedroom): x=8..14, z=10..15 with door at (8, 12)
            snapshot.AddRoom(new SpatialRoomSummary
            {
                RoomId = 1,
                Role = "Bedroom",
                CellCount = 20,
                BedCount = 1,
                CenterX = 11,
                CenterZ = 12,
                IsEnclosed = true
            });
            FillEnclosedRoom(snapshot, 8, 14, 10, 15, roomId: 1, doorX: 8, doorZ: 12);

            // Place existing bed inside bedroom away from door at (13, 14)
            snapshot.SetCell(13, 14, SpatialCellKind.OccupiedFurniture, roomId: 1, roofed: true);
            snapshot.Peers.Add(new SpatialPeerItem
            {
                DefName = "Bed",
                Category = SpatialBuildingCategory.Sleeping,
                X = 13,
                Z = 14,
                RoomId = 1,
                IsBlueprint = false
            });

            // Room 2 (Workshop): x=16..22, z=10..15 with door at (16, 12)
            snapshot.AddRoom(new SpatialRoomSummary
            {
                RoomId = 2,
                Role = "Workshop",
                CellCount = 20,
                WorktableCount = 1,
                CenterX = 19,
                CenterZ = 12,
                IsEnclosed = true
            });
            FillEnclosedRoom(snapshot, 16, 22, 10, 15, roomId: 2, doorX: 16, doorZ: 12);

            // Existing worktable at (20, 14)
            snapshot.SetCell(20, 14, SpatialCellKind.OccupiedFurniture, roomId: 2, roofed: true);
            snapshot.Peers.Add(new SpatialPeerItem
            {
                DefName = "TableButcher",
                Category = SpatialBuildingCategory.Production,
                X = 20,
                Z = 14,
                RoomId = 2,
                IsBlueprint = false
            });

            return snapshot;
        }

        private static void FillEnclosedRoom(
            ColonySpatialSnapshot snapshot,
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            int roomId,
            int doorX,
            int doorZ)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    bool isPerimeter = x == minX || x == maxX || z == minZ || z == maxZ;
                    if (x == doorX && z == doorZ)
                    {
                        snapshot.SetCell(x, z, SpatialCellKind.Door, roomId: 0, roofed: true);
                    }
                    else if (isPerimeter)
                    {
                        snapshot.SetCell(x, z, SpatialCellKind.Wall, roomId: 0, roofed: true);
                    }
                    else
                    {
                        snapshot.SetCell(x, z, SpatialCellKind.IndoorRoom, roomId: roomId, roofed: true);
                    }
                }
            }
        }

        [Fact]
        public void Bed_placement_prefers_enclosed_bedroom_wall_and_clusters_near_peer_bed_away_from_door()
        {
            ColonySpatialSnapshot snapshot = BuildBedroomAndWorkshopSnapshot();
            var request = new SpatialPlacementRequest
            {
                BuildingDefName = "Bed",
                Category = SpatialBuildingCategory.Sleeping,
                SizeX = 1,
                SizeZ = 1,
                LocationHint = "near_base",
                AnchorX = 2,
                AnchorZ = 2
            };

            List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(snapshot, request);
            Assert.NotEmpty(ranked);

            SpatialCandidateScore top = ranked[0];
            Assert.Equal(1, top.RoomId);
            Assert.True(top.X > 8 && top.X < 14);
            Assert.True(top.Z > 10 && top.Z < 15);
            Assert.NotEqual(9, top.X); // Not blocking the doorway at (8, 12) when top-right wall cells exist
        }

        [Fact]
        public void Worktable_placement_prefers_workshop_and_rejects_blocked_interaction_cell()
        {
            ColonySpatialSnapshot snapshot = BuildBedroomAndWorkshopSnapshot();
            var request = new SpatialPlacementRequest
            {
                BuildingDefName = "ElectricSmithy",
                Category = SpatialBuildingCategory.Production,
                SizeX = 2,
                SizeZ = 1,
                HasInteractionCell = true,
                InteractionOffsetX = 0,
                InteractionOffsetZ = -1,
                LocationHint = "near_base",
                AnchorX = 2,
                AnchorZ = 2
            };

            List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(snapshot, request);
            Assert.NotEmpty(ranked);

            SpatialCandidateScore top = ranked[0];
            Assert.Equal(2, top.RoomId);
            // Interaction cell at (top.X, top.Z - 1) must remain strictly passable (not Wall at z=10)
            Assert.True(top.Z > 11);
        }

        [Fact]
        public void Outdoor_location_hint_and_ascii_grid_renderer_work_deterministically()
        {
            ColonySpatialSnapshot snapshot = BuildBedroomAndWorkshopSnapshot();
            var outdoorReq = new SpatialPlacementRequest
            {
                BuildingDefName = "Campfire",
                Category = SpatialBuildingCategory.TemperatureLight,
                SizeX = 1,
                SizeZ = 1,
                LocationHint = "outdoor",
                AnchorX = 4,
                AnchorZ = 4
            };

            List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(snapshot, outdoorReq);
            Assert.NotEmpty(ranked);
            Assert.Equal(0, ranked[0].RoomId);

            string grid = ColonySpatialClusterSolver.RenderCompactAsciiGrid(snapshot, centerX: 11, centerZ: 12, radius: 6);
            Assert.Contains("Legend:", grid);
            Assert.Contains("#", grid);
            Assert.Contains("+", grid);
            Assert.Contains("B", grid);
            Assert.Contains("@", grid);
        }

        [Fact]
        public void CenterAnchored_MultiCell_Footprint_Rejects_Cells_Whose_Offset_Corners_Overlap_Walls()
        {
            ColonySpatialSnapshot snapshot = BuildBedroomAndWorkshopSnapshot();
            // 3x1 center-anchored workbench (occupied x-1..x+1): must never choose x=17 (adjacent to left wall x=16) or x=21 (adjacent to right wall x=22)
            var request = new SpatialPlacementRequest
            {
                BuildingDefName = "FueledStove",
                Category = SpatialBuildingCategory.Production,
                SizeX = 3,
                SizeZ = 1,
                FootprintMinOffsetX = -1,
                FootprintMinOffsetZ = 0,
                HasInteractionCell = true,
                InteractionOffsetX = 0,
                InteractionOffsetZ = -1,
                LocationHint = "indoor",
                AnchorX = 19,
                AnchorZ = 12
            };

            List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(snapshot, request);
            Assert.NotEmpty(ranked);
            foreach (var candidate in ranked)
            {
                if (candidate.RoomId == 2)
                {
                    Assert.InRange(candidate.X, 18, 20);
                }
            }
        }

        [Fact]
        public void GrowingZone_HardExclusion_Rejects_SingleAndMultiCell_Footprints_And_Renders_G_In_Ascii()
        {
            ColonySpatialSnapshot snapshot = BuildBedroomAndWorkshopSnapshot();
            // Mark a 4x4 growing zone patch at x=2..5, z=2..5
            for (int x = 2; x <= 5; x++)
            {
                for (int z = 2; z <= 5; z++)
                {
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false, storageZone: false, growingZone: true);
                }
            }

            // Single-cell request anchored directly inside the farm must never return any farm cell
            var singleReq = new SpatialPlacementRequest
            {
                BuildingDefName = "Campfire",
                Category = SpatialBuildingCategory.TemperatureLight,
                SizeX = 1,
                SizeZ = 1,
                LocationHint = "outdoor",
                AnchorX = 3,
                AnchorZ = 3,
                TargetX = 3,
                TargetZ = 3,
                AllowGrowingZone = false
            };

            List<SpatialCandidateScore> singleRanked = ColonySpatialClusterSolver.RankCandidates(snapshot, singleReq);
            Assert.NotEmpty(singleRanked);
            foreach (var c in singleRanked)
            {
                Assert.False(snapshot.IsGrowingZone(c.X, c.Z), $"Candidate ({c.X},{c.Z}) must never overlap a growing zone");
            }

            // 3x1 multi-cell request must reject any anchor whose footprint touches even 1 farm cell
            var multiReq = new SpatialPlacementRequest
            {
                BuildingDefName = "FueledStove",
                Category = SpatialBuildingCategory.Production,
                SizeX = 3,
                SizeZ = 1,
                FootprintMinOffsetX = -1,
                FootprintMinOffsetZ = 0,
                LocationHint = "outdoor",
                AnchorX = 4,
                AnchorZ = 4,
                AllowGrowingZone = false
            };

            List<SpatialCandidateScore> multiRanked = ColonySpatialClusterSolver.RankCandidates(snapshot, multiReq);
            Assert.NotEmpty(multiRanked);
            foreach (var c in multiRanked)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Assert.False(snapshot.IsGrowingZone(c.X + dx, c.Z),
                        $"Multi-cell candidate ({c.X},{c.Z}) footprint cell ({c.X + dx},{c.Z}) must not overlap growing zone");
                }
            }

            string ascii = SpatialAsciiRenderer.RenderCompactAsciiGrid(snapshot, centerX: 4, centerZ: 4, radius: 4);
            Assert.Contains("G=Farm(NoBuild)", ascii);
            Assert.Contains("G", ascii);
        }

        [Fact]
        public void Stockpile_MultiCell_Penalty_Strongly_Avoids_Stockpile_For_NonStorage_Buildings()
        {
            var snapshot = new ColonySpatialSnapshot(0, 0, 14, 14)
            {
                ColonyAnchorX = 5,
                ColonyAnchorZ = 5,
                HasBaseAnchor = true
            };
            for (int x = 0; x <= 13; x++)
            {
                for (int z = 0; z <= 13; z++)
                {
                    bool isStockpile = x >= 2 && x <= 7 && z >= 2 && z <= 7;
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false, storageZone: isStockpile);
                }
            }

            var nonStorageReq = new SpatialPlacementRequest
            {
                BuildingDefName = "FueledStove",
                Category = SpatialBuildingCategory.Production,
                SizeX = 2,
                SizeZ = 1,
                LocationHint = "outdoor",
                AnchorX = 5,
                AnchorZ = 5
            };

            List<SpatialCandidateScore> ranked = ColonySpatialClusterSolver.RankCandidates(snapshot, nonStorageReq);
            Assert.NotEmpty(ranked);
            SpatialCandidateScore top = ranked[0];
            // Neither cell of the 2x1 footprint may touch the stockpile when clean open tiles exist
            Assert.False(snapshot.IsStorageZone(top.X, top.Z));
            Assert.False(snapshot.IsStorageZone(top.X + 1, top.Z));
        }

        [Fact]
        public void PreferredRoomId_And_TargetCoordinates_Bias_Top_Ranked_Candidate()
        {
            ColonySpatialSnapshot snapshot = BuildBedroomAndWorkshopSnapshot();

            // Even though Bed naturally prefers Bedroom (Room 1), PreferredRoomId = 2 (+90) overrides it cleanly
            var roomReq = new SpatialPlacementRequest
            {
                BuildingDefName = "Bed",
                Category = SpatialBuildingCategory.Sleeping,
                SizeX = 1,
                SizeZ = 1,
                LocationHint = "near_base",
                AnchorX = 11,
                AnchorZ = 12,
                PreferredRoomId = 2
            };

            List<SpatialCandidateScore> roomRanked = ColonySpatialClusterSolver.RankCandidates(snapshot, roomReq);
            Assert.NotEmpty(roomRanked);
            Assert.Equal(2, roomRanked[0].RoomId);
            Assert.Equal("requested_room", roomRanked[0].Reason);

            // Explicit TargetX/TargetZ selects exact free cell with reason "exact_target"
            var targetReq = new SpatialPlacementRequest
            {
                BuildingDefName = "TableShort",
                Category = SpatialBuildingCategory.DiningRecreation,
                SizeX = 1,
                SizeZ = 1,
                LocationHint = "near_base",
                AnchorX = 2,
                AnchorZ = 2,
                TargetX = 18,
                TargetZ = 13
            };

            List<SpatialCandidateScore> targetRanked = ColonySpatialClusterSolver.RankCandidates(snapshot, targetReq);
            Assert.NotEmpty(targetRanked);
            Assert.Equal(18, targetRanked[0].X);
            Assert.Equal(13, targetRanked[0].Z);
            Assert.Equal("exact_target", targetRanked[0].Reason);
        }
    }
}
