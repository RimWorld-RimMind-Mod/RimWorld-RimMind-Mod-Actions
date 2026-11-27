using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace RimMind.Actions.Spatial
{
    public sealed class SpatialVisualRenderResult
    {
        public byte[] PngBytes { get; set; } = Array.Empty<byte>();
        public string Base64Png { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int TilePixels { get; set; }
        public int Radius { get; set; }
        public int GridMinX { get; set; }
        public int GridMaxX { get; set; }
        public int GridMinZ { get; set; }
        public int GridMaxZ { get; set; }
    }

    /// <summary>
    /// Pure-C# off-screen top-down spatial PNG renderer with zero Verse or UnityEngine dependencies.
    /// Produces compact, high-contrast annotated RGB PNG maps featuring color-coded rooms, walls, doors,
    /// diagonal-striped green growing zones, amber stockpiles, coordinate axes, legend bar, and
    /// Set-of-Mark numbered candidate footprints [1]..[6] readable by multimodal vision LLMs.
    /// </summary>
    public static class SpatialVisualMapRenderer
    {
        private const int MinRadius = 6;
        private const int MaxRadius = 26;
        private const int TopMargin = 32;
        private const int BottomMargin = 22;
        private const int LeftMargin = 34;
        private const int RightMargin = 10;

        private readonly struct Rgb
        {
            public readonly byte R;
            public readonly byte G;
            public readonly byte B;

            public Rgb(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
            }
        }

        // High-contrast palette
        private static readonly Rgb BgMargin = new Rgb(20, 24, 30);
        private static readonly Rgb AxisText = new Rgb(216, 224, 236);
        private static readonly Rgb AxisTick = new Rgb(130, 144, 166);
        private static readonly Rgb OutOfBoundsColor = new Rgb(14, 16, 20);
        private static readonly Rgb OutdoorFloorColor = new Rgb(48, 56, 68);
        private static readonly Rgb IndoorRoomColor = new Rgb(196, 206, 218);
        private static readonly Rgb GrowingBaseColor = new Rgb(44, 186, 72);
        private static readonly Rgb GrowingStripeColor = new Rgb(22, 126, 42);
        private static readonly Rgb StockpileBaseColor = new Rgb(222, 162, 42);
        private static readonly Rgb StockpileAccentColor = new Rgb(172, 118, 22);
        private static readonly Rgb WallFillColor = new Rgb(24, 28, 34);
        private static readonly Rgb WallBevelColor = new Rgb(62, 70, 82);
        private static readonly Rgb ImpassableBaseColor = new Rgb(76, 70, 64);
        private static readonly Rgb ImpassableHatchColor = new Rgb(56, 51, 46);
        private static readonly Rgb DoorFillColor = new Rgb(42, 194, 218);
        private static readonly Rgb DoorJambColor = new Rgb(18, 92, 108);
        private static readonly Rgb BlueprintBorderColor = new Rgb(76, 218, 250);
        private static readonly Rgb BedColor = new Rgb(56, 130, 236);
        private static readonly Rgb BedPillowColor = new Rgb(190, 218, 254);
        private static readonly Rgb WorktableColor = new Rgb(232, 112, 44);
        private static readonly Rgb TableRecColor = new Rgb(164, 98, 226);
        private static readonly Rgb StorageFurnColor = new Rgb(232, 196, 68);
        private static readonly Rgb TempLightColor = new Rgb(246, 214, 86);
        private static readonly Rgb DefenseColor = new Rgb(216, 64, 68);
        private static readonly Rgb GenericFurnColor = new Rgb(118, 136, 162);
        private static readonly Rgb CandidateOutlineColor = new Rgb(242, 42, 188);
        private static readonly Rgb CandidatePillBgColor = new Rgb(28, 10, 36);
        private static readonly Rgb CandidatePillBorderColor = new Rgb(255, 92, 212);
        private static readonly Rgb WhiteColor = new Rgb(255, 255, 255);
        private static readonly Rgb RoomBadgeBg = new Rgb(30, 38, 52);
        private static readonly Rgb RoomBadgeText = new Rgb(218, 228, 240);

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        public static SpatialVisualRenderResult Render(
            ColonySpatialSnapshot snapshot,
            int centerX,
            int centerZ,
            int radius = 16,
            IReadOnlyList<SpatialVisualCandidate>? candidates = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            int clampedRadius = Math.Max(MinRadius, Math.Min(MaxRadius, radius));
            int gridTiles = clampedRadius * 2 + 1;
            int tilePx = Math.Max(8, Math.Min(24, 400 / gridTiles));

            int gridWidthPx = gridTiles * tilePx;
            int gridHeightPx = gridTiles * tilePx;
            int width = LeftMargin + gridWidthPx + RightMargin;
            int height = TopMargin + gridHeightPx + BottomMargin;

            int minX = centerX - clampedRadius;
            int maxX = centerX + clampedRadius;
            int minZ = centerZ - clampedRadius;
            int maxZ = centerZ + clampedRadius;

            byte[] pixels = new byte[width * height * 3];
            FillRect(pixels, width, height, 0, 0, width, height, BgMargin);

            // Index peers by world (x, z) so furniture receives category-specific colors
            var peerMap = new Dictionary<long, SpatialPeerItem>(snapshot.Peers.Count);
            for (int i = 0; i < snapshot.Peers.Count; i++)
            {
                SpatialPeerItem peer = snapshot.Peers[i];
                peerMap[PackCoord(peer.X, peer.Z)] = peer;
            }

            // 1. Render base terrain, rooms, zones, walls, doors, blueprints, and furniture
            for (int x = minX; x <= maxX; x++)
            {
                int px0 = LeftMargin + (x - minX) * tilePx;
                for (int z = minZ; z <= maxZ; z++)
                {
                    int py0 = TopMargin + (maxZ - z) * tilePx;
                    RenderTile(pixels, width, height, snapshot, peerMap, x, z, px0, py0, tilePx);
                }
            }

            // 2. Render subtle 1px grid lines + emphasized 5-tile major grid lines
            RenderGridLines(pixels, width, height, minX, maxX, minZ, maxZ, tilePx);

            // 3. Render subtle view center reticle at (centerX, centerZ)
            int centerPx = LeftMargin + (centerX - minX) * tilePx;
            int centerPy = TopMargin + (maxZ - centerZ) * tilePx;
            DrawCenterReticle(pixels, width, height, centerPx, centerPy, tilePx);

            // 4. Render subtle room ID badges ("R1", "R2", ...) for enclosed rooms in viewport
            RenderRoomLabels(pixels, width, height, snapshot, minX, maxX, minZ, maxZ, tilePx);

            // 5. Render Set-of-Mark candidate overlays ([1]..[6]) on top of everything in the map area
            if (candidates != null && candidates.Count > 0)
            {
                RenderCandidates(pixels, width, height, candidates, minX, maxX, minZ, maxZ, tilePx);
            }

            // 6. Render outer map frame, coordinate axes, and top legend bar
            DrawRectOutline(pixels, width, height, LeftMargin - 1, TopMargin - 1, gridWidthPx + 2, gridHeightPx + 2, AxisTick, 1);
            RenderAxes(pixels, width, height, minX, maxX, minZ, maxZ, tilePx);
            RenderLegend(pixels, width, height);

            byte[] pngBytes = EncodePngRgb24(pixels, width, height);
            return new SpatialVisualRenderResult
            {
                PngBytes = pngBytes,
                Base64Png = Convert.ToBase64String(pngBytes),
                Width = width,
                Height = height,
                TilePixels = tilePx,
                Radius = clampedRadius,
                GridMinX = minX,
                GridMaxX = maxX,
                GridMinZ = minZ,
                GridMaxZ = maxZ
            };
        }

        public static byte[] RenderPngBytes(
            ColonySpatialSnapshot snapshot,
            int centerX,
            int centerZ,
            int radius = 16,
            IReadOnlyList<SpatialVisualCandidate>? candidates = null)
            => Render(snapshot, centerX, centerZ, radius, candidates).PngBytes;

        private static void RenderTile(
            byte[] pixels,
            int width,
            int height,
            ColonySpatialSnapshot snapshot,
            Dictionary<long, SpatialPeerItem> peerMap,
            int x,
            int z,
            int px0,
            int py0,
            int tilePx)
        {
            SpatialCellKind kind = snapshot.GetCellKind(x, z);
            if (kind == SpatialCellKind.OutOfBounds)
            {
                FillRect(pixels, width, height, px0, py0, tilePx, tilePx, OutOfBoundsColor);
                return;
            }

            bool isGrowing = snapshot.IsGrowingZone(x, z);
            bool isStockpile = snapshot.IsStorageZone(x, z);

            switch (kind)
            {
                case SpatialCellKind.Wall:
                    FillRect(pixels, width, height, px0, py0, tilePx, tilePx, WallFillColor);
                    if (tilePx >= 6)
                    {
                        FillRect(pixels, width, height, px0, py0, tilePx, 2, WallBevelColor);
                        FillRect(pixels, width, height, px0, py0, 2, tilePx, WallBevelColor);
                    }
                    return;

                case SpatialCellKind.Door:
                    FillRect(pixels, width, height, px0, py0, tilePx, tilePx, DoorFillColor);
                    DrawRectOutline(pixels, width, height, px0, py0, tilePx, tilePx, DoorJambColor, 1);
                    if (tilePx >= 8)
                    {
                        int mid = tilePx / 2;
                        FillRect(pixels, width, height, px0 + 2, py0 + mid - 1, tilePx - 4, 2, DoorJambColor);
                    }
                    return;

                case SpatialCellKind.Impassable:
                    FillDiagonalPattern(pixels, width, height, px0, py0, tilePx, tilePx, ImpassableBaseColor, ImpassableHatchColor, period: 5, stripeWidth: 1);
                    return;

                case SpatialCellKind.IndoorRoom:
                case SpatialCellKind.OutdoorFloor:
                    if (isGrowing)
                    {
                        FillDiagonalPattern(pixels, width, height, px0, py0, tilePx, tilePx, GrowingBaseColor, GrowingStripeColor, period: 6, stripeWidth: 2);
                    }
                    else if (isStockpile)
                    {
                        FillRect(pixels, width, height, px0, py0, tilePx, tilePx, StockpileBaseColor);
                        DrawRectOutline(pixels, width, height, px0 + 1, py0 + 1, Math.Max(1, tilePx - 2), Math.Max(1, tilePx - 2), StockpileAccentColor, 1);
                    }
                    else
                    {
                        FillRect(pixels, width, height, px0, py0, tilePx, tilePx,
                            kind == SpatialCellKind.IndoorRoom ? IndoorRoomColor : OutdoorFloorColor);
                    }
                    break;

                case SpatialCellKind.Blueprint:
                    FillRect(pixels, width, height, px0, py0, tilePx, tilePx,
                        snapshot.IsRoofed(x, z) ? IndoorRoomColor : OutdoorFloorColor);
                    DrawRectOutline(pixels, width, height, px0 + 1, py0 + 1, Math.Max(1, tilePx - 2), Math.Max(1, tilePx - 2), BlueprintBorderColor, Math.Min(2, tilePx / 4));
                    return;

                case SpatialCellKind.OccupiedFurniture:
                    FillRect(pixels, width, height, px0, py0, tilePx, tilePx,
                        snapshot.IsRoofed(x, z) ? IndoorRoomColor : OutdoorFloorColor);
                    break;
            }

            if (peerMap.TryGetValue(PackCoord(x, z), out SpatialPeerItem? peer) && peer != null)
            {
                if (peer.IsBlueprint)
                {
                    DrawRectOutline(pixels, width, height, px0 + 1, py0 + 1, Math.Max(1, tilePx - 2), Math.Max(1, tilePx - 2), BlueprintBorderColor, Math.Min(2, tilePx / 4));
                    return;
                }

                Rgb furnColor = peer.Category switch
                {
                    SpatialBuildingCategory.Sleeping => BedColor,
                    SpatialBuildingCategory.Production => WorktableColor,
                    SpatialBuildingCategory.DiningRecreation => TableRecColor,
                    SpatialBuildingCategory.Storage => StorageFurnColor,
                    SpatialBuildingCategory.TemperatureLight => TempLightColor,
                    SpatialBuildingCategory.Defense => DefenseColor,
                    _ => GenericFurnColor
                };

                int pad = tilePx >= 12 ? 2 : 1;
                FillRect(pixels, width, height, px0 + pad, py0 + pad, Math.Max(2, tilePx - pad * 2), Math.Max(2, tilePx - pad * 2), furnColor);
                if (peer.Category == SpatialBuildingCategory.Sleeping && tilePx >= 10)
                {
                    FillRect(pixels, width, height, px0 + pad + 1, py0 + pad + 1, Math.Max(2, tilePx - (pad + 1) * 2), Math.Max(2, tilePx / 4), BedPillowColor);
                }
            }
            else if (kind == SpatialCellKind.OccupiedFurniture)
            {
                int pad = tilePx >= 12 ? 2 : 1;
                FillRect(pixels, width, height, px0 + pad, py0 + pad, Math.Max(2, tilePx - pad * 2), Math.Max(2, tilePx - pad * 2), GenericFurnColor);
            }
        }

        private static void RenderGridLines(
            byte[] pixels,
            int width,
            int height,
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            int tilePx)
        {
            int gridWidthPx = (maxX - minX + 1) * tilePx;
            int gridHeightPx = (maxZ - minZ + 1) * tilePx;

            for (int x = minX; x <= maxX; x++)
            {
                int px = LeftMargin + (x - minX) * tilePx;
                bool major = (x % 5) == 0;
                float alpha = major ? 0.28f : 0.12f;
                Rgb lineColor = major ? WhiteColor : WallFillColor;
                for (int py = TopMargin; py < TopMargin + gridHeightPx; py++)
                {
                    BlendPixel(pixels, width, height, px, py, lineColor, alpha);
                }
            }

            for (int z = minZ; z <= maxZ; z++)
            {
                int py = TopMargin + (maxZ - z) * tilePx;
                bool major = (z % 5) == 0;
                float alpha = major ? 0.28f : 0.12f;
                Rgb lineColor = major ? WhiteColor : WallFillColor;
                for (int px = LeftMargin; px < LeftMargin + gridWidthPx; px++)
                {
                    BlendPixel(pixels, width, height, px, py, lineColor, alpha);
                }
            }
        }

        private static void DrawCenterReticle(byte[] pixels, int width, int height, int px0, int py0, int tilePx)
        {
            int len = Math.Max(2, Math.Min(4, tilePx / 3));
            // Four corner ticks around view center tile
            FillRect(pixels, width, height, px0, py0, len, 1, WhiteColor);
            FillRect(pixels, width, height, px0, py0, 1, len, WhiteColor);
            FillRect(pixels, width, height, px0 + tilePx - len, py0, len, 1, WhiteColor);
            FillRect(pixels, width, height, px0 + tilePx - 1, py0, 1, len, WhiteColor);
            FillRect(pixels, width, height, px0, py0 + tilePx - 1, len, 1, WhiteColor);
            FillRect(pixels, width, height, px0, py0 + tilePx - len, 1, len, WhiteColor);
            FillRect(pixels, width, height, px0 + tilePx - len, py0 + tilePx - 1, len, 1, WhiteColor);
            FillRect(pixels, width, height, px0 + tilePx - 1, py0 + tilePx - len, 1, len, WhiteColor);
        }

        private static void RenderRoomLabels(
            byte[] pixels,
            int width,
            int height,
            ColonySpatialSnapshot snapshot,
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            int tilePx)
        {
            if (tilePx < 10) return;

            for (int i = 0; i < snapshot.Rooms.Count; i++)
            {
                SpatialRoomSummary room = snapshot.Rooms[i];
                if (!room.IsEnclosed || room.RoomId <= 0) continue;
                if (room.CenterX < minX || room.CenterX > maxX || room.CenterZ < minZ || room.CenterZ > maxZ) continue;

                string label = "R" + room.RoomId.ToString();
                int textW = MeasureTextWidth(label, scale: 1);
                int badgeW = textW + 4;
                int badgeH = 11;
                int cx = LeftMargin + (room.CenterX - minX) * tilePx + tilePx / 2;
                int cy = TopMargin + (maxZ - room.CenterZ) * tilePx + tilePx / 2;
                int bx = Math.Max(LeftMargin + 1, Math.Min(width - RightMargin - badgeW - 1, cx - badgeW / 2));
                int by = Math.Max(TopMargin + 1, Math.Min(height - BottomMargin - badgeH - 1, cy - badgeH / 2));

                FillRect(pixels, width, height, bx, by, badgeW, badgeH, RoomBadgeBg);
                DrawText(pixels, width, height, bx + 2, by + 2, label, RoomBadgeText, scale: 1);
            }
        }

        private static void RenderCandidates(
            byte[] pixels,
            int width,
            int height,
            IReadOnlyList<SpatialVisualCandidate> candidates,
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            int tilePx)
        {
            int gridLeft = LeftMargin;
            int gridTop = TopMargin;
            int gridRight = LeftMargin + (maxX - minX + 1) * tilePx;
            int gridBottom = TopMargin + (maxZ - minZ + 1) * tilePx;

            for (int i = 0; i < candidates.Count; i++)
            {
                SpatialVisualCandidate c = candidates[i];
                if (c == null) continue;

                int sizeX = Math.Max(1, c.SizeX);
                int sizeZ = Math.Max(1, c.SizeZ);
                int fpMinX = c.X + c.FootprintMinOffsetX;
                int fpMaxX = fpMinX + sizeX - 1;
                int fpMinZ = c.Z + c.FootprintMinOffsetZ;
                int fpMaxZ = fpMinZ + sizeZ - 1;

                if (fpMaxX < minX || fpMinX > maxX || fpMaxZ < minZ || fpMinZ > maxZ)
                    continue;

                int rawLeft = LeftMargin + (fpMinX - minX) * tilePx;
                int rawRight = LeftMargin + (fpMaxX - minX + 1) * tilePx;
                int rawTop = TopMargin + (maxZ - fpMaxZ) * tilePx;
                int rawBottom = TopMargin + (maxZ - fpMinZ + 1) * tilePx;

                int x0 = Math.Max(gridLeft, rawLeft);
                int x1 = Math.Min(gridRight, rawRight);
                int y0 = Math.Max(gridTop, rawTop);
                int y1 = Math.Min(gridBottom, rawBottom);
                int boxW = x1 - x0;
                int boxH = y1 - y0;
                if (boxW <= 0 || boxH <= 0) continue;

                // Subtle translucent magenta tint across full footprint
                for (int py = y0; py < y1; py++)
                {
                    for (int px = x0; px < x1; px++)
                    {
                        BlendPixel(pixels, width, height, px, py, CandidateOutlineColor, 0.25f);
                    }
                }

                // Crisp 2px bright magenta footprint outline
                DrawRectOutline(pixels, width, height, x0, y0, boxW, boxH, CandidateOutlineColor, thickness: 2);

                // High-contrast dark pill with crisp white digit [1..6]
                string badgeText = c.CandidateId.ToString();
                int scale = tilePx >= 20 ? 2 : 1;
                int textW = MeasureTextWidth(badgeText, scale);
                int textH = 7 * scale;
                int pillW = textW + 6;
                int pillH = textH + 4;
                int pillX = Math.Max(gridLeft + 1, Math.Min(gridRight - pillW - 1, (x0 + x1 - pillW) / 2));
                int pillY = Math.Max(gridTop + 1, Math.Min(gridBottom - pillH - 1, (y0 + y1 - pillH) / 2));

                FillRect(pixels, width, height, pillX, pillY, pillW, pillH, CandidatePillBgColor);
                DrawRectOutline(pixels, width, height, pillX, pillY, pillW, pillH, CandidatePillBorderColor, thickness: 1);
                DrawText(pixels, width, height, pillX + 3, pillY + 2, badgeText, WhiteColor, scale);
            }
        }

        private static void RenderAxes(
            byte[] pixels,
            int width,
            int height,
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            int tilePx)
        {
            int gridHeightPx = (maxZ - minZ + 1) * tilePx;
            int tickStep = tilePx >= 16 ? 2 : (tilePx >= 10 ? 4 : 5);

            // Bottom X axis labels
            int xAxisY = TopMargin + gridHeightPx;
            for (int x = minX; x <= maxX; x++)
            {
                if (((x - minX) % tickStep) != 0 && x != maxX) continue;
                int cx = LeftMargin + (x - minX) * tilePx + tilePx / 2;
                FillRect(pixels, width, height, cx, xAxisY + 1, 1, 3, AxisTick);

                string label = x.ToString();
                int tw = MeasureTextWidth(label, 1);
                int tx = Math.Max(LeftMargin, Math.Min(width - RightMargin - tw, cx - tw / 2));
                DrawText(pixels, width, height, tx, xAxisY + 6, label, AxisText, 1);
            }

            // Left Z axis labels (North = top = maxZ)
            for (int z = minZ; z <= maxZ; z++)
            {
                if (((maxZ - z) % tickStep) != 0 && z != minZ) continue;
                int cy = TopMargin + (maxZ - z) * tilePx + tilePx / 2;
                FillRect(pixels, width, height, LeftMargin - 4, cy, 3, 1, AxisTick);

                string label = z.ToString();
                int tw = MeasureTextWidth(label, 1);
                int tx = Math.Max(2, LeftMargin - 6 - tw);
                int ty = Math.Max(TopMargin, Math.Min(TopMargin + gridHeightPx - 7, cy - 3));
                DrawText(pixels, width, height, tx, ty, label, AxisText, 1);
            }
        }

        private static void RenderLegend(byte[] pixels, int width, int height)
        {
            int x = 6;
            x = DrawLegendItem(pixels, width, height, x, 4, GrowingBaseColor, "FARM");
            x = DrawLegendItem(pixels, width, height, x, 4, StockpileBaseColor, "STOCK");
            x = DrawLegendItem(pixels, width, height, x, 4, IndoorRoomColor, "ROOM");
            x = DrawLegendItem(pixels, width, height, x, 4, WallFillColor, "WALL");
            DrawLegendItem(pixels, width, height, x, 4, DoorFillColor, "DOOR");

            int x2 = 6;
            x2 = DrawLegendItem(pixels, width, height, x2, 17, BedColor, "BED");
            x2 = DrawLegendItem(pixels, width, height, x2, 17, WorktableColor, "WORK");
            x2 = DrawLegendItem(pixels, width, height, x2, 17, BlueprintBorderColor, "BP");
            DrawLegendItem(pixels, width, height, x2, 17, CandidateOutlineColor, "1-6 CAND");
        }

        private static int DrawLegendItem(
            byte[] pixels,
            int width,
            int height,
            int x,
            int y,
            Rgb swatch,
            string text)
        {
            if (x + 12 >= width) return x;
            FillRect(pixels, width, height, x, y, 8, 8, swatch);
            DrawRectOutline(pixels, width, height, x, y, 8, 8, AxisTick, 1);
            DrawText(pixels, width, height, x + 11, y + 1, text, AxisText, 1);
            return x + 11 + MeasureTextWidth(text, 1) + 9;
        }

        private static void FillRect(byte[] pixels, int width, int height, int x0, int y0, int w, int h, Rgb color)
        {
            int startX = Math.Max(0, x0);
            int startY = Math.Max(0, y0);
            int endX = Math.Min(width, x0 + w);
            int endY = Math.Min(height, y0 + h);
            for (int py = startY; py < endY; py++)
            {
                int rowOff = py * width * 3;
                for (int px = startX; px < endX; px++)
                {
                    int idx = rowOff + px * 3;
                    pixels[idx] = color.R;
                    pixels[idx + 1] = color.G;
                    pixels[idx + 2] = color.B;
                }
            }
        }

        private static void FillDiagonalPattern(
            byte[] pixels,
            int width,
            int height,
            int x0,
            int y0,
            int w,
            int h,
            Rgb baseColor,
            Rgb stripeColor,
            int period,
            int stripeWidth)
        {
            int startX = Math.Max(0, x0);
            int startY = Math.Max(0, y0);
            int endX = Math.Min(width, x0 + w);
            int endY = Math.Min(height, y0 + h);
            for (int py = startY; py < endY; py++)
            {
                int rowOff = py * width * 3;
                for (int px = startX; px < endX; px++)
                {
                    Rgb c = ((px + py) % period) < stripeWidth ? stripeColor : baseColor;
                    int idx = rowOff + px * 3;
                    pixels[idx] = c.R;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.B;
                }
            }
        }

        private static void DrawRectOutline(
            byte[] pixels,
            int width,
            int height,
            int x0,
            int y0,
            int w,
            int h,
            Rgb color,
            int thickness)
        {
            int t = Math.Max(1, thickness);
            FillRect(pixels, width, height, x0, y0, w, t, color);
            FillRect(pixels, width, height, x0, y0 + h - t, w, t, color);
            FillRect(pixels, width, height, x0, y0, t, h, color);
            FillRect(pixels, width, height, x0 + w - t, y0, t, h, color);
        }

        private static void BlendPixel(byte[] pixels, int width, int height, int px, int py, Rgb overlay, float alpha)
        {
            if (px < 0 || px >= width || py < 0 || py >= height) return;
            int idx = (py * width + px) * 3;
            float inv = 1f - alpha;
            pixels[idx] = (byte)Math.Max(0, Math.Min(255, (int)(pixels[idx] * inv + overlay.R * alpha)));
            pixels[idx + 1] = (byte)Math.Max(0, Math.Min(255, (int)(pixels[idx + 1] * inv + overlay.G * alpha)));
            pixels[idx + 2] = (byte)Math.Max(0, Math.Min(255, (int)(pixels[idx + 2] * inv + overlay.B * alpha)));
        }

        private static int MeasureTextWidth(string text, int scale)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (text.Length * 6 - 1) * Math.Max(1, scale);
        }

        private static void DrawText(byte[] pixels, int width, int height, int x, int y, string text, Rgb color, int scale)
        {
            int s = Math.Max(1, scale);
            int cursorX = x;
            for (int i = 0; i < text.Length; i++)
            {
                byte[]? glyph = GetGlyph5x7(char.ToUpperInvariant(text[i]));
                if (glyph != null)
                {
                    for (int row = 0; row < 7; row++)
                    {
                        byte bits = glyph[row];
                        for (int col = 0; col < 5; col++)
                        {
                            if ((bits & (1 << (4 - col))) != 0)
                            {
                                FillRect(pixels, width, height, cursorX + col * s, y + row * s, s, s, color);
                            }
                        }
                    }
                }
                cursorX += 6 * s;
            }
        }

        private static byte[]? GetGlyph5x7(char ch)
        {
            return ch switch
            {
                '0' => new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
                '1' => new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
                '2' => new byte[] { 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F },
                '3' => new byte[] { 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E },
                '4' => new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
                '5' => new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
                '6' => new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
                '7' => new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
                '8' => new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
                '9' => new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
                'A' => new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
                'B' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
                'C' => new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
                'D' => new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C },
                'E' => new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
                'F' => new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
                'G' => new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
                'H' => new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
                'I' => new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
                'J' => new byte[] { 0x07, 0x02, 0x02, 0x02, 0x12, 0x12, 0x0C },
                'K' => new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
                'L' => new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
                'M' => new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
                'N' => new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 },
                'O' => new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
                'P' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
                'Q' => new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
                'R' => new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
                'S' => new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
                'T' => new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
                'U' => new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
                'V' => new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
                'W' => new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 },
                'X' => new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
                'Y' => new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
                'Z' => new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
                '-' => new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
                '+' => new byte[] { 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00 },
                ':' => new byte[] { 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x00 },
                _ => null
            };
        }

        private static byte[] EncodePngRgb24(byte[] rgbPixels, int width, int height)
        {
            int stride = width * 3;
            byte[] rawScanlines = new byte[height * (1 + stride)];
            for (int y = 0; y < height; y++)
            {
                int dstRow = y * (1 + stride);
                rawScanlines[dstRow] = 0; // Filter type 0 (None)
                Buffer.BlockCopy(rgbPixels, y * stride, rawScanlines, dstRow + 1, stride);
            }

            byte[] zlibPayload = CompressZlib(rawScanlines);

            using (var output = new MemoryStream(zlibPayload.Length + 64))
            {
                output.Write(PngSignature, 0, PngSignature.Length);

                byte[] ihdr = new byte[13];
                WriteBigEndianUInt32(ihdr, 0, (uint)width);
                WriteBigEndianUInt32(ihdr, 4, (uint)height);
                ihdr[8] = 8; // Bit depth
                ihdr[9] = 2; // Color type: Truecolor RGB
                ihdr[10] = 0; // Compression: deflate
                ihdr[11] = 0; // Filter: adaptive
                ihdr[12] = 0; // Interlace: none
                WriteChunk(output, "IHDR", ihdr);

                WriteChunk(output, "IDAT", zlibPayload);
                WriteChunk(output, "IEND", Array.Empty<byte>());

                return output.ToArray();
            }
        }

        private static byte[] CompressZlib(byte[] rawData)
        {
            uint adler32 = ComputeAdler32(rawData);
            using (var ms = new MemoryStream(rawData.Length / 4 + 32))
            {
                // RFC 1950 zlib header: CMF=0x78 (deflate, 32K window), FLG=0x9C (default compression, FCHECK valid)
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);

                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    deflate.Write(rawData, 0, rawData.Length);
                }

                byte[] trailer = new byte[4];
                WriteBigEndianUInt32(trailer, 0, adler32);
                ms.Write(trailer, 0, 4);
                return ms.ToArray();
            }
        }

        private static uint ComputeAdler32(byte[] data)
        {
            const uint ModAdler = 65521u;
            uint a = 1u;
            uint b = 0u;
            int index = 0;
            int length = data.Length;

            while (length > 0)
            {
                int chunk = length > 5552 ? 5552 : length;
                length -= chunk;
                while (chunk-- > 0)
                {
                    a += data[index++];
                    b += a;
                }
                a %= ModAdler;
                b %= ModAdler;
            }

            return (b << 16) | a;
        }

        private static void WriteChunk(Stream output, string chunkType, byte[] data)
        {
            byte[] header = new byte[8];
            WriteBigEndianUInt32(header, 0, (uint)data.Length);
            header[4] = (byte)chunkType[0];
            header[5] = (byte)chunkType[1];
            header[6] = (byte)chunkType[2];
            header[7] = (byte)chunkType[3];
            output.Write(header, 0, 8);

            if (data.Length > 0)
                output.Write(data, 0, data.Length);

            uint crc = 0xFFFFFFFFu;
            for (int i = 4; i < 8; i++)
                crc = Crc32Table[(crc ^ header[i]) & 0xFFu] ^ (crc >> 8);
            for (int i = 0; i < data.Length; i++)
                crc = Crc32Table[(crc ^ data[i]) & 0xFFu] ^ (crc >> 8);
            crc ^= 0xFFFFFFFFu;

            byte[] crcBytes = new byte[4];
            WriteBigEndianUInt32(crcBytes, 0, crc);
            output.Write(crcBytes, 0, 4);
        }

        private static void WriteBigEndianUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1u) != 0 ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
                }
                table[n] = c;
            }
            return table;
        }

        private static long PackCoord(int x, int z) => (((long)x) << 32) | (uint)z;
    }
}
