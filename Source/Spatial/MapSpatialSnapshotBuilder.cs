#if !RIMMIND_ACTIONS_TESTS
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimMind.Actions.Spatial
{
    public static class MapSpatialSnapshotBuilder
    {
        private sealed class RoomAccumulator
        {
            public int RoomId;
            public int CellCount;
            public long SumX;
            public long SumZ;
            public int BedCount;
            public int WorktableCount;
            public int TableCount;
        }

        public static ColonySpatialSnapshot Build(Map map, int fallbackX, int fallbackZ, int radius = 26)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            map.regionAndRoomUpdater?.TryRebuildDirtyRegionsAndRooms();

            int clampedRadius = Math.Max(8, Math.Min(32, radius));
            DetermineColonyAnchor(map, fallbackX, fallbackZ, out int anchorX, out int anchorZ, out bool hasBaseAnchor);

            int minX = Math.Max(0, anchorX - clampedRadius);
            int maxX = Math.Min(map.Size.x - 1, anchorX + clampedRadius);
            int minZ = Math.Max(0, anchorZ - clampedRadius);
            int maxZ = Math.Min(map.Size.z - 1, anchorZ + clampedRadius);

            var snapshot = new ColonySpatialSnapshot(minX, minZ, maxX - minX + 1, maxZ - minZ + 1)
            {
                ColonyAnchorX = anchorX,
                ColonyAnchorZ = anchorZ,
                HasBaseAnchor = hasBaseAnchor
            };

            var roomAccum = new Dictionary<int, RoomAccumulator>();
            var roofGrid = map.roofGrid;
            var zoneManager = map.zoneManager;

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    bool fogged = cell.Fogged(map);
                    Room? room = fogged ? null : cell.GetRoom(map);
                    bool enclosed = room != null && !room.PsychologicallyOutdoors && !room.TouchesMapEdge;
                    int roomId = enclosed ? room!.ID : 0;
                    bool roofed = roofGrid != null && roofGrid.Roofed(cell);
                    bool isStorage = zoneManager != null && zoneManager.ZoneAt(cell) is Zone_Stockpile;

                    SpatialCellKind kind;
                    if (fogged)
                    {
                        kind = SpatialCellKind.Impassable;
                    }
                    else
                    {
                        Building edifice = cell.GetEdifice(map);
                        if (edifice != null)
                        {
                            if (edifice is Building_Door)
                                kind = SpatialCellKind.Door;
                            else if (edifice.def.building != null && edifice.def.building.isNaturalRock)
                                kind = SpatialCellKind.Impassable;
                            else if (edifice.def.passability == Traversability.Impassable)
                                kind = SpatialCellKind.Wall;
                            else
                                kind = SpatialCellKind.OccupiedFurniture;
                        }
                        else if (!cell.Standable(map))
                        {
                            kind = SpatialCellKind.Impassable;
                        }
                        else if (cell.GetFirstThing<Blueprint>(map) != null || cell.GetFirstThing<Frame>(map) != null)
                        {
                            kind = SpatialCellKind.Blueprint;
                        }
                        else if (enclosed || roofed)
                        {
                            kind = SpatialCellKind.IndoorRoom;
                        }
                        else
                        {
                            kind = SpatialCellKind.OutdoorFloor;
                        }
                    }

                    snapshot.SetCell(x, z, kind, roomId, roofed, isStorage);

                    if (enclosed && roomId > 0)
                    {
                        if (!roomAccum.TryGetValue(roomId, out var acc))
                        {
                            acc = new RoomAccumulator { RoomId = roomId };
                            roomAccum[roomId] = acc;
                        }
                        acc.CellCount++;
                        acc.SumX += x;
                        acc.SumZ += z;
                    }
                }
            }

            var buildings = map.listerBuildings?.allBuildingsColonist;
            if (buildings != null)
            {
                int limit = Math.Min(buildings.Count, 300);
                for (int i = 0; i < limit; i++)
                {
                    Building b = buildings[i];
                    if (b == null || !b.Spawned || b.Destroyed) continue;
                    IntVec3 pos = b.Position;
                    if (!snapshot.InBounds(pos.x, pos.z)) continue;

                    bool isBed = b is Building_Bed;
                    bool isWorkTable = b is Building_WorkTable;
                    SpatialBuildingCategory cat = ColonySpatialClusterSolver.ClassifyBuilding(
                        b.def?.defName,
                        isBed: isBed,
                        isWorkTable: isWorkTable);

                    int roomId = snapshot.GetRoomId(pos.x, pos.z);
                    snapshot.Peers.Add(new SpatialPeerItem
                    {
                        DefName = b.def?.defName ?? string.Empty,
                        Category = cat,
                        X = pos.x,
                        Z = pos.z,
                        RoomId = roomId,
                        IsBlueprint = false
                    });

                    if (roomId > 0 && roomAccum.TryGetValue(roomId, out var acc))
                    {
                        if (isBed || cat == SpatialBuildingCategory.Sleeping) acc.BedCount++;
                        if (isWorkTable || cat == SpatialBuildingCategory.Production) acc.WorktableCount++;
                        if (cat == SpatialBuildingCategory.DiningRecreation) acc.TableCount++;
                    }
                }
            }

            var blueprints = map.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint);
            if (blueprints != null)
            {
                int limit = Math.Min(blueprints.Count, 150);
                for (int i = 0; i < limit; i++)
                {
                    Thing bp = blueprints[i];
                    if (bp == null || !bp.Spawned || bp.Faction != Faction.OfPlayer) continue;
                    IntVec3 pos = bp.Position;
                    if (!snapshot.InBounds(pos.x, pos.z)) continue;

                    string buildDefName = (bp as Blueprint_Build)?.def?.entityDefToBuild?.defName ?? bp.def?.defName ?? string.Empty;
                    SpatialBuildingCategory cat = ColonySpatialClusterSolver.ClassifyBuilding(buildDefName);
                    int roomId = snapshot.GetRoomId(pos.x, pos.z);
                    snapshot.Peers.Add(new SpatialPeerItem
                    {
                        DefName = buildDefName,
                        Category = cat,
                        X = pos.x,
                        Z = pos.z,
                        RoomId = roomId,
                        IsBlueprint = true
                    });

                    if (roomId > 0 && roomAccum.TryGetValue(roomId, out var acc))
                    {
                        if (cat == SpatialBuildingCategory.Sleeping) acc.BedCount++;
                        if (cat == SpatialBuildingCategory.Production) acc.WorktableCount++;
                        if (cat == SpatialBuildingCategory.DiningRecreation) acc.TableCount++;
                    }
                }
            }

            foreach (var kv in roomAccum)
            {
                var acc = kv.Value;
                if (acc.CellCount <= 0) continue;
                string role = InferRoomRole(acc);
                snapshot.AddRoom(new SpatialRoomSummary
                {
                    RoomId = acc.RoomId,
                    Role = role,
                    CellCount = acc.CellCount,
                    BedCount = acc.BedCount,
                    WorktableCount = acc.WorktableCount,
                    TableCount = acc.TableCount,
                    CenterX = (int)(acc.SumX / acc.CellCount),
                    CenterZ = (int)(acc.SumZ / acc.CellCount),
                    IsEnclosed = true
                });
            }

            return snapshot;
        }

        private static string InferRoomRole(RoomAccumulator acc)
        {
            if (acc.BedCount > 1) return "Barracks";
            if (acc.BedCount == 1) return "Bedroom";
            if (acc.WorktableCount > 0) return "Workshop";
            if (acc.TableCount > 0) return "Dining";
            return "Indoor";
        }

        private static void DetermineColonyAnchor(
            Map map,
            int fallbackX,
            int fallbackZ,
            out int anchorX,
            out int anchorZ,
            out bool hasBaseAnchor)
        {
            long sumX = 0;
            long sumZ = 0;
            int count = 0;

            var buildings = map.listerBuildings?.allBuildingsColonist;
            if (buildings != null)
            {
                int limit = Math.Min(buildings.Count, 200);
                for (int i = 0; i < limit; i++)
                {
                    Building b = buildings[i];
                    if (b == null || !b.Spawned || b.Destroyed) continue;
                    if (b.def != null && b.def.passability == Traversability.Impassable) continue;
                    sumX += b.Position.x;
                    sumZ += b.Position.z;
                    count++;
                }

                if (count == 0)
                {
                    for (int i = 0; i < limit; i++)
                    {
                        Building b = buildings[i];
                        if (b == null || !b.Spawned || b.Destroyed) continue;
                        sumX += b.Position.x;
                        sumZ += b.Position.z;
                        count++;
                    }
                }
            }

            if (count == 0 && map.zoneManager?.AllZones != null)
            {
                foreach (Zone z in map.zoneManager.AllZones)
                {
                    if (z is Zone_Stockpile && z.Cells.Count > 0)
                    {
                        sumX += z.Position.x;
                        sumZ += z.Position.z;
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                anchorX = (int)(sumX / count);
                anchorZ = (int)(sumZ / count);
                hasBaseAnchor = true;
                return;
            }

            anchorX = fallbackX;
            anchorZ = fallbackZ;
            hasBaseAnchor = false;
        }
    }
}
#endif
