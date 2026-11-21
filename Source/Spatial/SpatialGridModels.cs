using System;
using System.Collections.Generic;

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
        /// <summary>
        /// Offset from the candidate placement anchor (x, z) to the minimum X/Z corner of the building footprint.
        /// Defaults to 0 (bottom-left anchored); RimWorld center-anchored footprints set this from <c>GenAdj.OccupiedRect</c>.
        /// </summary>
        public int FootprintMinOffsetX { get; set; }
        public int FootprintMinOffsetZ { get; set; }
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
}
