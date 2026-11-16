using System;
using System.Collections.Generic;
using System.Text;

namespace RimMind.Actions.Spatial
{
    public enum SpatialCellKind
    {
        OutOfBounds = 0,
        Impassable = 1,
        Wall = 2,
        Door = 3,
        OutdoorFloor = 4,
        IndoorRoom = 5,
        OccupiedFurniture = 6,
        Blueprint = 7
    }

    public enum SpatialBuildingCategory
    {
        General = 0,
        Sleeping = 1,
        Production = 2,
        DiningRecreation = 3,
        TemperatureLight = 4,
        Storage = 5,
        Defense = 6,
        Structure = 7
    }

    public sealed class SpatialRoomSummary
    {
        public int RoomId { get; set; }
        public string Role { get; set; } = "Indoor";
        public int CellCount { get; set; }
        public int BedCount { get; set; }
        public int WorktableCount { get; set; }
        public int TableCount { get; set; }
        public int CenterX { get; set; }
        public int CenterZ { get; set; }
        public bool IsEnclosed { get; set; }
    }

    public sealed class SpatialPeerItem
    {
        public string DefName { get; set; } = string.Empty;
        public SpatialBuildingCategory Category { get; set; }
        public int X { get; set; }
        public int Z { get; set; }
        public int RoomId { get; set; }
        public bool IsBlueprint { get; set; }
    }

    public sealed class SpatialPlacementRequest
    {
        public string BuildingDefName { get; set; } = string.Empty;
        public SpatialBuildingCategory Category { get; set; }
        public int SizeX { get; set; } = 1;
        public int SizeZ { get; set; } = 1;
        public bool HasInteractionCell { get; set; }
        public int InteractionOffsetX { get; set; }
        public int InteractionOffsetZ { get; set; }
        public string LocationHint { get; set; } = "near_base";
        public int AnchorX { get; set; }
        public int AnchorZ { get; set; }
    }

    public sealed class SpatialCandidateScore
    {
        public int X { get; set; }
        public int Z { get; set; }
        public double Score { get; set; }
        public int RoomId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class ColonySpatialSnapshot
    {
        private readonly SpatialCellKind[,] _cells;
        private readonly int[,] _roomIds;
        private readonly bool[,] _roofed;
        private readonly bool[,] _storageZone;
        private readonly Dictionary<int, SpatialRoomSummary> _roomMap = new Dictionary<int, SpatialRoomSummary>();

        public ColonySpatialSnapshot(int minX, int minZ, int width, int height)
        {
            MinX = minX;
            MinZ = minZ;
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            _cells = new SpatialCellKind[Width, Height];
            _roomIds = new int[Width, Height];
            _roofed = new bool[Width, Height];
            _storageZone = new bool[Width, Height];
        }

        public int MinX { get; }
        public int MinZ { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaxX => MinX + Width - 1;
        public int MaxZ => MinZ + Height - 1;

        public int ColonyAnchorX { get; set; }
        public int ColonyAnchorZ { get; set; }
        public bool HasBaseAnchor { get; set; }

        public List<SpatialRoomSummary> Rooms { get; } = new List<SpatialRoomSummary>();
        public List<SpatialPeerItem> Peers { get; } = new List<SpatialPeerItem>();

        public bool InBounds(int x, int z)
            => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        public void SetCell(int x, int z, SpatialCellKind kind, int roomId = 0, bool roofed = false, bool storageZone = false)
        {
            if (!InBounds(x, z)) return;
            int ix = x - MinX;
            int iz = z - MinZ;
            _cells[ix, iz] = kind;
            _roomIds[ix, iz] = roomId;
            _roofed[ix, iz] = roofed;
            _storageZone[ix, iz] = storageZone;
        }

        public SpatialCellKind GetCellKind(int x, int z)
            => InBounds(x, z) ? _cells[x - MinX, z - MinZ] : SpatialCellKind.OutOfBounds;

        public int GetRoomId(int x, int z)
            => InBounds(x, z) ? _roomIds[x - MinX, z - MinZ] : 0;

        public bool IsRoofed(int x, int z)
            => InBounds(x, z) && _roofed[x - MinX, z - MinZ];

        public bool IsStorageZone(int x, int z)
            => InBounds(x, z) && _storageZone[x - MinX, z - MinZ];

        public void AddRoom(SpatialRoomSummary room)
        {
            if (room == null) return;
            Rooms.Add(room);
            _roomMap[room.RoomId] = room;
        }

        public bool TryGetRoom(int roomId, out SpatialRoomSummary room)
            => _roomMap.TryGetValue(roomId, out room!);
    }

    public static class ColonySpatialClusterSolver
    {
        public static SpatialBuildingCategory ClassifyBuilding(
            string? defName,
            bool isBed = false,
            bool isWorkTable = false,
            bool isTable = false,
            bool isJoy = false,
            bool isDefense = false)
        {
            if (isBed) return SpatialBuildingCategory.Sleeping;
            if (isWorkTable) return SpatialBuildingCategory.Production;
            if (isTable || isJoy) return SpatialBuildingCategory.DiningRecreation;
            if (isDefense) return SpatialBuildingCategory.Defense;

            string key = (defName ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Contains("bed") || key.Contains("sleep"))
                return SpatialBuildingCategory.Sleeping;
            if (key.Contains("stove") || key.Contains("bench") || key.Contains("table_")
                || key.Contains("butcher") || key.Contains("research") || key.Contains("smith")
                || key.Contains("tailor") || key.Contains("machining") || key.Contains("fabrication")
                || key.Contains("brewery") || key.Contains("stonecutter"))
                return SpatialBuildingCategory.Production;
            if (key.Contains("table") || key.Contains("chair") || key.Contains("stool")
                || key.Contains("horseshoe") || key.Contains("chess") || key.Contains("poker"))
                return SpatialBuildingCategory.DiningRecreation;
            if (key.Contains("cooler") || key.Contains("heater") || key.Contains("campfire")
                || key.Contains("torch") || key.Contains("lamp") || key.Contains("brazier"))
                return SpatialBuildingCategory.TemperatureLight;
            if (key.Contains("shelf") || key.Contains("storage") || key.Contains("hopper"))
                return SpatialBuildingCategory.Storage;
            if (key.Contains("turret") || key.Contains("sandbag") || key.Contains("barricade") || key.Contains("trap"))
                return SpatialBuildingCategory.Defense;
            if (key.Contains("wall") || key.Contains("door"))
                return SpatialBuildingCategory.Structure;

            return SpatialBuildingCategory.General;
        }

        public static List<SpatialCandidateScore> RankCandidates(
            ColonySpatialSnapshot snapshot,
            SpatialPlacementRequest request,
            Func<int, int, bool>? extraValidator = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (request == null) throw new ArgumentNullException(nameof(request));

            int sizeX = Math.Max(1, request.SizeX);
            int sizeZ = Math.Max(1, request.SizeZ);
            string hint = (request.LocationHint ?? "near_base").Trim().ToLowerInvariant();

            int targetX = hint == "near_pawn" || !snapshot.HasBaseAnchor
                ? request.AnchorX
                : snapshot.ColonyAnchorX;
            int targetZ = hint == "near_pawn" || !snapshot.HasBaseAnchor
                ? request.AnchorZ
                : snapshot.ColonyAnchorZ;

            var candidates = new List<SpatialCandidateScore>();

            for (int x = snapshot.MinX; x <= snapshot.MaxX; x++)
            {
                for (int z = snapshot.MinZ; z <= snapshot.MaxZ; z++)
                {
                    if (!IsFootprintFree(snapshot, x, z, sizeX, sizeZ))
                        continue;

                    if (request.HasInteractionCell)
                    {
                        int ix = x + request.InteractionOffsetX;
                        int iz = z + request.InteractionOffsetZ;
                        if (!IsCellPassableForInteraction(snapshot.GetCellKind(ix, iz)))
                            continue;
                    }

                    if (extraValidator != null && !extraValidator(x, z))
                        continue;

                    SpatialCandidateScore scored = ScoreCandidate(snapshot, request, hint, x, z, sizeX, sizeZ, targetX, targetZ);
                    candidates.Add(scored);
                }
            }

            candidates.Sort((a, b) =>
            {
                int scoreCmp = b.Score.CompareTo(a.Score);
                if (scoreCmp != 0) return scoreCmp;
                int distA = Math.Abs(a.X - targetX) + Math.Abs(a.Z - targetZ);
                int distB = Math.Abs(b.X - targetX) + Math.Abs(b.Z - targetZ);
                int distCmp = distA.CompareTo(distB);
                if (distCmp != 0) return distCmp;
                int xCmp = a.X.CompareTo(b.X);
                return xCmp != 0 ? xCmp : a.Z.CompareTo(b.Z);
            });

            return candidates;
        }

        private static bool IsFootprintFree(ColonySpatialSnapshot snapshot, int x, int z, int sizeX, int sizeZ)
        {
            for (int dx = 0; dx < sizeX; dx++)
            {
                for (int dz = 0; dz < sizeZ; dz++)
                {
                    SpatialCellKind kind = snapshot.GetCellKind(x + dx, z + dz);
                    if (kind != SpatialCellKind.IndoorRoom && kind != SpatialCellKind.OutdoorFloor)
                        return false;
                }
            }
            return true;
        }

        private static bool IsCellPassableForInteraction(SpatialCellKind kind)
            => kind == SpatialCellKind.IndoorRoom || kind == SpatialCellKind.OutdoorFloor || kind == SpatialCellKind.Door;

        private static SpatialCandidateScore ScoreCandidate(
            ColonySpatialSnapshot snapshot,
            SpatialPlacementRequest request,
            string hint,
            int x,
            int z,
            int sizeX,
            int sizeZ,
            int targetX,
            int targetZ)
        {
            SpatialCellKind cellKind = snapshot.GetCellKind(x, z);
            int roomId = snapshot.GetRoomId(x, z);
            SpatialRoomSummary? room = null;
            bool inEnclosedRoom = roomId > 0 && snapshot.TryGetRoom(roomId, out room) && room.IsEnclosed;
            bool isRoofed = snapshot.IsRoofed(x, z);
            bool inStorage = snapshot.IsStorageZone(x, z);

            int adjWalls = CountAdjacentKind(snapshot, x, z, sizeX, sizeZ, SpatialCellKind.Wall);
            int adjDoors = CountAdjacentKind(snapshot, x, z, sizeX, sizeZ, SpatialCellKind.Door);

            double score = 0d;
            string reason = "open_ground";

            bool wantsOutdoor = hint == "outdoor" || request.Category == SpatialBuildingCategory.Defense;
            bool wantsIndoor = hint == "indoor"
                || request.Category == SpatialBuildingCategory.Sleeping
                || request.Category == SpatialBuildingCategory.Production
                || request.Category == SpatialBuildingCategory.DiningRecreation
                || request.Category == SpatialBuildingCategory.Storage;

            if (wantsOutdoor)
            {
                if (cellKind == SpatialCellKind.OutdoorFloor)
                {
                    score += 45d;
                    reason = "outdoor_clear";
                }
                else
                {
                    score -= 60d;
                }
            }
            else if (wantsIndoor)
            {
                if (inEnclosedRoom)
                {
                    score += 65d;
                    reason = "enclosed_room";
                }
                else if (isRoofed && cellKind == SpatialCellKind.IndoorRoom)
                {
                    score += 35d;
                    reason = "roofed_interior";
                }
                else if (hint == "indoor")
                {
                    score -= 30d;
                }
            }
            else if (inEnclosedRoom)
            {
                score += 20d;
                reason = "enclosed_room";
            }

            if (adjDoors > 0)
                score -= 35d * adjDoors;

            if (inStorage && request.Category != SpatialBuildingCategory.Storage)
                score -= 18d;

            if (request.Category == SpatialBuildingCategory.Sleeping)
            {
                if (room != null && room.IsEnclosed)
                {
                    if (room.BedCount > 0)
                    {
                        score += 35d;
                        reason = "cluster_bedroom";
                    }
                    else if (room.WorktableCount == 0 && room.TableCount == 0)
                    {
                        score += 22d;
                        reason = "empty_enclosed_room";
                    }
                    else if (room.WorktableCount > 0)
                    {
                        score -= 12d;
                    }
                }

                if (adjWalls >= 1 && adjWalls <= 2)
                {
                    score += 25d;
                    if (reason == "open_ground" || reason == "enclosed_room")
                        reason = "indoor_wall_aligned";
                }

                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Sleeping, x, z, maxDist: 4, bonus: 22d);
            }
            else if (request.Category == SpatialBuildingCategory.Production)
            {
                if (room != null && room.IsEnclosed && room.WorktableCount > 0)
                {
                    score += 35d;
                    reason = "cluster_workshop";
                }
                else if (inEnclosedRoom && (room == null || room.BedCount == 0))
                {
                    score += 18d;
                }

                if (adjWalls == 1)
                    score += 15d;

                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Production, x, z, maxDist: 5, bonus: 20d);
                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Storage, x, z, maxDist: 6, bonus: 12d);
            }
            else if (request.Category == SpatialBuildingCategory.DiningRecreation)
            {
                if (room != null && room.IsEnclosed && room.TableCount > 0)
                {
                    score += 35d;
                    reason = "cluster_dining";
                }

                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.DiningRecreation, x, z, maxDist: 4, bonus: 20d);
                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Production, x, z, maxDist: 7, bonus: 8d);
            }
            else if (request.Category == SpatialBuildingCategory.Storage)
            {
                if (inEnclosedRoom)
                {
                    score += 25d;
                    reason = "indoor_storage";
                }
                if (adjWalls >= 1) score += 10d;
                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Storage, x, z, maxDist: 4, bonus: 20d);
            }
            else if (request.Category == SpatialBuildingCategory.TemperatureLight)
            {
                if (inEnclosedRoom)
                {
                    score += 40d;
                    if (room != null && (room.BedCount > 0 || room.WorktableCount > 0))
                    {
                        score += 25d;
                        reason = "serve_occupied_room";
                    }
                }
            }

            if (hint == "near_pawn")
            {
                int pawnDist = Math.Abs(x - request.AnchorX) + Math.Abs(z - request.AnchorZ);
                score -= pawnDist * 1.2d;
            }
            else
            {
                int anchorDist = Math.Abs(x - targetX) + Math.Abs(z - targetZ);
                score -= anchorDist * 0.45d;
            }

            return new SpatialCandidateScore
            {
                X = x,
                Z = z,
                Score = score,
                RoomId = roomId,
                Reason = reason
            };
        }

        private static double PeerClusterBonus(
            IReadOnlyList<SpatialPeerItem> peers,
            SpatialBuildingCategory category,
            int x,
            int z,
            int maxDist,
            double bonus)
        {
            double best = 0d;
            for (int i = 0; i < peers.Count; i++)
            {
                SpatialPeerItem peer = peers[i];
                if (peer.Category != category) continue;
                int dist = Math.Abs(peer.X - x) + Math.Abs(peer.Z - z);
                if (dist == 1)
                    best = Math.Max(best, bonus * 0.75d);
                else if (dist >= 2 && dist <= maxDist)
                    best = Math.Max(best, bonus);
            }
            return best;
        }

        private static int CountAdjacentKind(
            ColonySpatialSnapshot snapshot,
            int x,
            int z,
            int sizeX,
            int sizeZ,
            SpatialCellKind targetKind)
        {
            int count = 0;
            for (int dx = 0; dx < sizeX; dx++)
            {
                if (snapshot.GetCellKind(x + dx, z - 1) == targetKind) count++;
                if (snapshot.GetCellKind(x + dx, z + sizeZ) == targetKind) count++;
            }
            for (int dz = 0; dz < sizeZ; dz++)
            {
                if (snapshot.GetCellKind(x - 1, z + dz) == targetKind) count++;
                if (snapshot.GetCellKind(x + sizeX, z + dz) == targetKind) count++;
            }
            return count;
        }

        public static string RenderCompactAsciiGrid(
            ColonySpatialSnapshot snapshot,
            int centerX,
            int centerZ,
            int radius = 10)
        {
            if (snapshot == null) return string.Empty;
            int clampedRadius = Math.Max(2, Math.Min(16, radius));
            var sb = new StringBuilder();
            sb.Append("Center=(").Append(centerX).Append(',').Append(centerZ).Append(") r=").Append(clampedRadius)
              .AppendLine(" Legend: #=Wall +=Door B=Bed W=Work T=Table S=Storage *=Blueprint .=Indoor ,=Outdoor X=Blocked @=Center");

            var peerGlyph = new Dictionary<long, char>();
            for (int i = 0; i < snapshot.Peers.Count; i++)
            {
                SpatialPeerItem p = snapshot.Peers[i];
                char g = p.IsBlueprint ? '*' : p.Category switch
                {
                    SpatialBuildingCategory.Sleeping => 'B',
                    SpatialBuildingCategory.Production => 'W',
                    SpatialBuildingCategory.DiningRecreation => 'T',
                    SpatialBuildingCategory.Storage => 'S',
                    _ => 'O'
                };
                peerGlyph[PackCoord(p.X, p.Z)] = g;
            }

            for (int z = centerZ + clampedRadius; z >= centerZ - clampedRadius; z--)
            {
                sb.Append(z.ToString().PadLeft(3)).Append("| ");
                for (int x = centerX - clampedRadius; x <= centerX + clampedRadius; x++)
                {
                    if (peerGlyph.TryGetValue(PackCoord(x, z), out char g))
                    {
                        sb.Append(g);
                        continue;
                    }

                    if (x == centerX && z == centerZ)
                    {
                        sb.Append('@');
                        continue;
                    }

                    SpatialCellKind kind = snapshot.GetCellKind(x, z);
                    char ch = kind switch
                    {
                        SpatialCellKind.Wall => '#',
                        SpatialCellKind.Door => '+',
                        SpatialCellKind.Blueprint => '*',
                        SpatialCellKind.OccupiedFurniture => 'O',
                        SpatialCellKind.IndoorRoom => '.',
                        SpatialCellKind.OutdoorFloor => snapshot.IsStorageZone(x, z) ? 'S' : ',',
                        SpatialCellKind.Impassable => 'X',
                        _ => ' '
                    };
                    sb.Append(ch);
                }
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static long PackCoord(int x, int z) => (((long)x) << 32) | (uint)z;
    }
}
