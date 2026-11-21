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
    }
}
