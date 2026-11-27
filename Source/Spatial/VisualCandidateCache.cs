using System;
using System.Collections.Generic;

namespace RimMind.Actions.Spatial
{
    /// <summary>
    /// Thread-safe, bounded, engine-free cache storing recent Set-of-Mark visual placement
    /// candidates returned by <c>actions_inspect_map_view</c>, keyed by <c>(mapId, buildingDef)</c>.
    /// Entries expire after <see cref="MaxFreshnessTicks"/> game ticks so stale layouts are never placed blindly.
    /// </summary>
    public static class VisualCandidateCache
    {
        public const int MaxFreshnessTicks = 1800;
        private const int MaxCacheEntries = 64;

        private sealed class CacheEntry
        {
            public IReadOnlyList<SpatialVisualCandidate> Candidates { get; set; } = Array.Empty<SpatialVisualCandidate>();
            public int SnapshotTick { get; set; }
            public long Sequence { get; set; }
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly int MapId;
            public readonly string BuildingDefKey;

            public CacheKey(int mapId, string buildingDefKey)
            {
                MapId = mapId;
                BuildingDefKey = buildingDefKey;
            }

            public bool Equals(CacheKey other)
                => MapId == other.MapId && string.Equals(BuildingDefKey, other.BuildingDefKey, StringComparison.Ordinal);

            public override bool Equals(object? obj)
                => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (MapId * 397) ^ StringComparer.Ordinal.GetHashCode(BuildingDefKey ?? string.Empty);
                }
            }
        }

        private static readonly object SyncLock = new object();
        private static readonly Dictionary<CacheKey, CacheEntry> Entries = new Dictionary<CacheKey, CacheEntry>();
        private static long _nextSequence;

        public static void Store(
            int mapId,
            string? buildingDef,
            IReadOnlyList<SpatialVisualCandidate>? candidates,
            int currentTick)
        {
            if (string.IsNullOrWhiteSpace(buildingDef) || candidates == null)
                return;

            var key = new CacheKey(mapId, NormalizeDef(buildingDef!));
            var copy = new List<SpatialVisualCandidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                SpatialVisualCandidate c = candidates[i];
                if (c == null) continue;
                copy.Add(new SpatialVisualCandidate
                {
                    CandidateId = c.CandidateId,
                    X = c.X,
                    Z = c.Z,
                    RotationByte = c.RotationByte,
                    Rotation = c.Rotation,
                    SizeX = c.SizeX,
                    SizeZ = c.SizeZ,
                    FootprintMinOffsetX = c.FootprintMinOffsetX,
                    FootprintMinOffsetZ = c.FootprintMinOffsetZ,
                    RoomId = c.RoomId,
                    Reason = c.Reason,
                    Score = c.Score
                });
            }

            lock (SyncLock)
            {
                if (!Entries.ContainsKey(key) && Entries.Count >= MaxCacheEntries)
                {
                    EvictOldestUnlocked();
                }

                Entries[key] = new CacheEntry
                {
                    Candidates = copy,
                    SnapshotTick = currentTick,
                    Sequence = ++_nextSequence
                };
            }
        }

        public static bool TryGetCandidate(
            int mapId,
            string? buildingDef,
            int candidateId,
            int currentTick,
            out SpatialVisualCandidate? candidate)
        {
            candidate = null;
            if (candidateId < 1 || string.IsNullOrWhiteSpace(buildingDef))
                return false;

            if (!TryGetCandidates(mapId, buildingDef, currentTick, out IReadOnlyList<SpatialVisualCandidate> list))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].CandidateId == candidateId)
                {
                    candidate = list[i];
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetCandidates(
            int mapId,
            string? buildingDef,
            int currentTick,
            out IReadOnlyList<SpatialVisualCandidate> candidates)
        {
            candidates = Array.Empty<SpatialVisualCandidate>();
            if (string.IsNullOrWhiteSpace(buildingDef))
                return false;

            var key = new CacheKey(mapId, NormalizeDef(buildingDef!));
            lock (SyncLock)
            {
                if (!Entries.TryGetValue(key, out CacheEntry? entry) || entry == null)
                    return false;

                int age = currentTick - entry.SnapshotTick;
                if (age < 0 || age >= MaxFreshnessTicks)
                {
                    Entries.Remove(key);
                    return false;
                }

                candidates = entry.Candidates;
                return true;
            }
        }

        public static void Clear()
        {
            lock (SyncLock)
            {
                Entries.Clear();
                _nextSequence = 0;
            }
        }

        private static string NormalizeDef(string buildingDef)
            => buildingDef.Trim().ToLowerInvariant();

        private static void EvictOldestUnlocked()
        {
            bool found = false;
            CacheKey oldestKey = default;
            long oldestSeq = long.MaxValue;

            foreach (KeyValuePair<CacheKey, CacheEntry> kv in Entries)
            {
                if (kv.Value.Sequence < oldestSeq)
                {
                    oldestSeq = kv.Value.Sequence;
                    oldestKey = kv.Key;
                    found = true;
                }
            }

            if (found)
                Entries.Remove(oldestKey);
        }
    }
}
