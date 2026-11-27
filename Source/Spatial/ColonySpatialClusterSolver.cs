using System;
using System.Collections.Generic;

namespace RimMind.Actions.Spatial
{
    /// <summary>
    /// Scores and ranks candidate placement coordinates inside a <see cref="ColonySpatialSnapshot"/>
    /// using room affinity, wall/door adjacency, interaction-cell passability, and peer clustering heuristics.
    /// </summary>
    public static class ColonySpatialClusterSolver
    {
        public static SpatialBuildingCategory ClassifyBuilding(
            string? defName,
            bool isBed = false,
            bool isWorkTable = false,
            bool isTable = false,
            bool isJoy = false,
            bool isDefense = false)
            => SpatialBuildingClassifier.Classify(defName, isBed, isWorkTable, isTable, isJoy, isDefense);

        public static string RenderCompactAsciiGrid(
            ColonySpatialSnapshot snapshot,
            int centerX,
            int centerZ,
            int radius = 10)
            => SpatialAsciiRenderer.RenderCompactAsciiGrid(snapshot, centerX, centerZ, radius);

        public static List<SpatialCandidateScore> RankCandidates(
            ColonySpatialSnapshot snapshot,
            SpatialPlacementRequest request,
            Func<int, int, bool>? extraValidator = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (request == null) throw new ArgumentNullException(nameof(request));

            int sizeX = Math.Max(1, request.SizeX);
            int sizeZ = Math.Max(1, request.SizeZ);
            int minOffX = request.FootprintMinOffsetX;
            int minOffZ = request.FootprintMinOffsetZ;
            string hint = (request.LocationHint ?? "near_base").Trim().ToLowerInvariant();

            bool hasExplicitTarget = request.TargetX.HasValue && request.TargetZ.HasValue;
            int targetX = hasExplicitTarget
                ? request.TargetX!.Value
                : (hint == "near_pawn" || !snapshot.HasBaseAnchor
                    ? request.AnchorX
                    : snapshot.ColonyAnchorX);
            int targetZ = hasExplicitTarget
                ? request.TargetZ!.Value
                : (hint == "near_pawn" || !snapshot.HasBaseAnchor
                    ? request.AnchorZ
                    : snapshot.ColonyAnchorZ);

            var candidates = new List<SpatialCandidateScore>();

            for (int x = snapshot.MinX; x <= snapshot.MaxX; x++)
            {
                for (int z = snapshot.MinZ; z <= snapshot.MaxZ; z++)
                {
                    int fpMinX = x + minOffX;
                    int fpMinZ = z + minOffZ;

                    if (!IsFootprintFree(snapshot, fpMinX, fpMinZ, sizeX, sizeZ, request.AllowGrowingZone))
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

                    SpatialCandidateScore scored = ScoreCandidate(
                        snapshot, request, hint, x, z, fpMinX, fpMinZ, sizeX, sizeZ, targetX, targetZ);
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

        private static bool IsFootprintFree(
            ColonySpatialSnapshot snapshot,
            int minX,
            int minZ,
            int sizeX,
            int sizeZ,
            bool allowGrowingZone)
        {
            for (int dx = 0; dx < sizeX; dx++)
            {
                for (int dz = 0; dz < sizeZ; dz++)
                {
                    int cx = minX + dx;
                    int cz = minZ + dz;
                    SpatialCellKind kind = snapshot.GetCellKind(cx, cz);
                    if (kind != SpatialCellKind.IndoorRoom && kind != SpatialCellKind.OutdoorFloor)
                        return false;
                    if (!allowGrowingZone && snapshot.IsGrowingZone(cx, cz))
                        return false;
                }
            }
            return true;
        }

        private static bool HasFootprintStorageOverlap(
            ColonySpatialSnapshot snapshot,
            int minX,
            int minZ,
            int sizeX,
            int sizeZ)
        {
            for (int dx = 0; dx < sizeX; dx++)
            {
                for (int dz = 0; dz < sizeZ; dz++)
                {
                    if (snapshot.IsStorageZone(minX + dx, minZ + dz))
                        return true;
                }
            }
            return false;
        }

        private static bool IsCellPassableForInteraction(SpatialCellKind kind)
            => kind == SpatialCellKind.IndoorRoom || kind == SpatialCellKind.OutdoorFloor || kind == SpatialCellKind.Door;

        private static SpatialCandidateScore ScoreCandidate(
            ColonySpatialSnapshot snapshot,
            SpatialPlacementRequest request,
            string hint,
            int x,
            int z,
            int fpMinX,
            int fpMinZ,
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
            bool overlapsStorage = HasFootprintStorageOverlap(snapshot, fpMinX, fpMinZ, sizeX, sizeZ);

            int adjWalls = CountAdjacentKind(snapshot, fpMinX, fpMinZ, sizeX, sizeZ, SpatialCellKind.Wall);
            int adjDoors = CountAdjacentKind(snapshot, fpMinX, fpMinZ, sizeX, sizeZ, SpatialCellKind.Door);

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

            if (overlapsStorage && request.Category != SpatialBuildingCategory.Storage)
                score -= 85d;

            score += ScoreCategoryAffinity(snapshot, request.Category, room, inEnclosedRoom, adjWalls, x, z, ref reason);

            if (request.PreferredRoomId.HasValue && request.PreferredRoomId.Value > 0 && roomId == request.PreferredRoomId.Value)
            {
                score += 90d;
                reason = "requested_room";
            }

            if (request.TargetX.HasValue && request.TargetZ.HasValue)
            {
                int targetDist = Math.Abs(x - request.TargetX.Value) + Math.Abs(z - request.TargetZ.Value);
                score += Math.Max(0d, 140d - targetDist * 18d);
                if (targetDist == 0)
                    reason = "exact_target";
                else if (targetDist <= 3)
                    reason = "near_requested_target";
            }

            if (hint == "near_pawn" && !(request.TargetX.HasValue && request.TargetZ.HasValue))
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

        private static double ScoreCategoryAffinity(
            ColonySpatialSnapshot snapshot,
            SpatialBuildingCategory category,
            SpatialRoomSummary? room,
            bool inEnclosedRoom,
            int adjWalls,
            int x,
            int z,
            ref string reason)
        {
            double score = 0d;

            if (category == SpatialBuildingCategory.Sleeping)
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
            else if (category == SpatialBuildingCategory.Production)
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
            else if (category == SpatialBuildingCategory.DiningRecreation)
            {
                if (room != null && room.IsEnclosed && room.TableCount > 0)
                {
                    score += 35d;
                    reason = "cluster_dining";
                }

                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.DiningRecreation, x, z, maxDist: 4, bonus: 20d);
                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Production, x, z, maxDist: 7, bonus: 8d);
            }
            else if (category == SpatialBuildingCategory.Storage)
            {
                if (inEnclosedRoom)
                {
                    score += 25d;
                    reason = "indoor_storage";
                }
                if (adjWalls >= 1) score += 10d;
                score += PeerClusterBonus(snapshot.Peers, SpatialBuildingCategory.Storage, x, z, maxDist: 4, bonus: 20d);
            }
            else if (category == SpatialBuildingCategory.TemperatureLight)
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

            return score;
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
            int minX,
            int minZ,
            int sizeX,
            int sizeZ,
            SpatialCellKind targetKind)
        {
            int count = 0;
            for (int dx = 0; dx < sizeX; dx++)
            {
                if (snapshot.GetCellKind(minX + dx, minZ - 1) == targetKind) count++;
                if (snapshot.GetCellKind(minX + dx, minZ + sizeZ) == targetKind) count++;
            }
            for (int dz = 0; dz < sizeZ; dz++)
            {
                if (snapshot.GetCellKind(minX - 1, minZ + dz) == targetKind) count++;
                if (snapshot.GetCellKind(minX + sizeX, minZ + dz) == targetKind) count++;
            }
            return count;
        }
    }
}
