using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Actions.Spatial;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    [Collection("VisualCandidateCache")]
    public sealed class SpatialVisualMapRendererTests
    {
        private static ColonySpatialSnapshot BuildRichColonySnapshot()
        {
            var snapshot = new ColonySpatialSnapshot(0, 0, 25, 25)
            {
                ColonyAnchorX = 12,
                ColonyAnchorZ = 12,
                HasBaseAnchor = true
            };

            for (int x = 0; x <= 24; x++)
            {
                for (int z = 0; z <= 24; z++)
                {
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false);
                }
            }

            // Enclosed bedroom (Room 1): x=9..15, z=10..16, door at (12, 10)
            snapshot.AddRoom(new SpatialRoomSummary
            {
                RoomId = 1,
                Role = "Bedroom",
                CellCount = 25,
                BedCount = 1,
                CenterX = 12,
                CenterZ = 13,
                IsEnclosed = true
            });
            for (int x = 9; x <= 15; x++)
            {
                for (int z = 10; z <= 16; z++)
                {
                    bool perimeter = x == 9 || x == 15 || z == 10 || z == 16;
                    if (x == 12 && z == 10)
                        snapshot.SetCell(x, z, SpatialCellKind.Door, roomId: 0, roofed: true);
                    else if (perimeter)
                        snapshot.SetCell(x, z, SpatialCellKind.Wall, roomId: 0, roofed: true);
                    else
                        snapshot.SetCell(x, z, SpatialCellKind.IndoorRoom, roomId: 1, roofed: true);
                }
            }
            snapshot.SetCell(14, 15, SpatialCellKind.OccupiedFurniture, roomId: 1, roofed: true);
            snapshot.Peers.Add(new SpatialPeerItem
            {
                DefName = "Bed",
                Category = SpatialBuildingCategory.Sleeping,
                X = 14,
                Z = 15,
                RoomId = 1,
                IsBlueprint = false
            });

            // Growing zone (farm): x=3..6, z=4..7
            for (int x = 3; x <= 6; x++)
            {
                for (int z = 4; z <= 7; z++)
                {
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false, storageZone: false, growingZone: true);
                }
            }

            // Stockpile zone: x=17..20, z=4..7
            for (int x = 17; x <= 20; x++)
            {
                for (int z = 4; z <= 7; z++)
                {
                    snapshot.SetCell(x, z, SpatialCellKind.OutdoorFloor, roomId: 0, roofed: false, storageZone: true, growingZone: false);
                }
            }

            return snapshot;
        }

        [Fact]
        public void Render_Produces_SpecValid_Png_With_Valid_Crc32_Zlib_Scanlines_And_Distinct_Zone_And_Candidate_Colors()
        {
            ColonySpatialSnapshot snapshot = BuildRichColonySnapshot();
            var candidates = new List<SpatialVisualCandidate>
            {
                new SpatialVisualCandidate
                {
                    CandidateId = 1,
                    X = 10,
                    Z = 14,
                    SizeX = 2,
                    SizeZ = 1,
                    RoomId = 1,
                    Reason = "cluster_bedroom",
                    Score = 112.5
                }
            };

            SpatialVisualRenderResult rendered = SpatialVisualMapRenderer.Render(
                snapshot, centerX: 12, centerZ: 12, radius: 10, candidates: candidates);

            Assert.NotNull(rendered.PngBytes);
            Assert.True(rendered.PngBytes.Length > 200);
            Assert.Equal(Convert.ToBase64String(rendered.PngBytes), rendered.Base64Png);

            // Verify 8-byte PNG signature
            byte[] expectedSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < expectedSig.Length; i++)
            {
                Assert.Equal(expectedSig[i], rendered.PngBytes[i]);
            }

            // Parse every PNG chunk, verify IEEE CRC-32, verify IHDR/IDAT/IEND, and decompress IDAT with .NET ZLibStream
            byte[] rgbPixels = DecodeAndVerifyPngRgb24(rendered.PngBytes, out int decodedW, out int decodedH);
            Assert.Equal(rendered.Width, decodedW);
            Assert.Equal(rendered.Height, decodedH);

            bool hasBrightGreenFarm = false;
            bool hasDarkGreenFarmStripe = false;
            bool hasAmberStockpile = false;
            bool hasMagentaCandidate = false;

            for (int i = 0; i < rgbPixels.Length; i += 3)
            {
                byte r = rgbPixels[i];
                byte g = rgbPixels[i + 1];
                byte b = rgbPixels[i + 2];

                if (g >= 170 && r <= 70 && b <= 90)
                    hasBrightGreenFarm = true;
                if (g >= 110 && g <= 145 && r <= 40 && b <= 60)
                    hasDarkGreenFarmStripe = true;
                if (r >= 200 && g >= 145 && g <= 180 && b <= 65)
                    hasAmberStockpile = true;
                if (r >= 220 && g <= 70 && b >= 170)
                    hasMagentaCandidate = true;
            }

            Assert.True(hasBrightGreenFarm, "Expected vibrant green pixels for Zone_Growing farm");
            Assert.True(hasDarkGreenFarmStripe, "Expected darker green diagonal stripe pixels for Zone_Growing farm");
            Assert.True(hasAmberStockpile, "Expected amber pixels for Zone_Stockpile");
            Assert.True(hasMagentaCandidate, "Expected bright magenta Set-of-Mark outline pixels for candidate [1]");
        }

        [Fact]
        public void Render_ZoomRadius_Scales_TilePixels_And_Clamps_Bounds()
        {
            ColonySpatialSnapshot snapshot = BuildRichColonySnapshot();

            SpatialVisualRenderResult zoomedIn = SpatialVisualMapRenderer.Render(snapshot, 12, 12, radius: 8);
            SpatialVisualRenderResult zoomedOut = SpatialVisualMapRenderer.Render(snapshot, 12, 12, radius: 18);
            SpatialVisualRenderResult clampedMin = SpatialVisualMapRenderer.Render(snapshot, 12, 12, radius: 2);
            SpatialVisualRenderResult clampedMax = SpatialVisualMapRenderer.Render(snapshot, 12, 12, radius: 40);

            Assert.True(zoomedIn.TilePixels > zoomedOut.TilePixels,
                $"Expected zoomed-in tilePx ({zoomedIn.TilePixels}) > zoomed-out tilePx ({zoomedOut.TilePixels})");
            Assert.Equal(6, clampedMin.Radius);
            Assert.Equal(26, clampedMax.Radius);
        }

        [Fact]
        public void VisualCandidateCache_Enforces_Freshness_MapIsolation_And_DefNormalization()
        {
            VisualCandidateCache.Clear();
            try
            {
                var list = new List<SpatialVisualCandidate>
                {
                    new SpatialVisualCandidate { CandidateId = 1, X = 10, Z = 12, RotationByte = 1, Rotation = "East", RoomId = 1 }
                };

                VisualCandidateCache.Store(mapId: 7, buildingDef: "Bed", candidates: list, currentTick: 5000);

                // Case-insensitive lookup within freshness window succeeds
                Assert.True(VisualCandidateCache.TryGetCandidate(7, "  bed ", 1, 5000 + VisualCandidateCache.MaxFreshnessTicks - 1, out var hit));
                Assert.NotNull(hit);
                Assert.Equal(10, hit!.X);
                Assert.Equal(12, hit.Z);
                Assert.Equal((byte)1, hit.RotationByte);

                // Different mapId or wrong candidateId misses
                Assert.False(VisualCandidateCache.TryGetCandidate(8, "Bed", 1, 5100, out _));
                Assert.False(VisualCandidateCache.TryGetCandidate(7, "Bed", 2, 5100, out _));

                // Re-store and verify expiry at exact MaxFreshnessTicks boundary (1800 ticks)
                VisualCandidateCache.Store(mapId: 7, buildingDef: "Bed", candidates: list, currentTick: 5000);
                Assert.False(VisualCandidateCache.TryGetCandidate(7, "Bed", 1, 5000 + VisualCandidateCache.MaxFreshnessTicks, out _));

                // Negative tick delta (e.g. reloaded earlier save) also rejected
                VisualCandidateCache.Store(mapId: 7, buildingDef: "Bed", candidates: list, currentTick: 5000);
                Assert.False(VisualCandidateCache.TryGetCandidate(7, "Bed", 1, 4999, out _));
            }
            finally
            {
                VisualCandidateCache.Clear();
            }
        }

        [Fact]
        public async Task InspectMapViewCompositeTool_Returns_PngAttachment_JsonMetadata_AsciiFallback_And_Caches_NonFarm_Candidates()
        {
            VisualCandidateCache.Clear();
            PlaceBlueprintCompositeTool.TestCurrentTick = 1000;
            try
            {
                var tool = new InspectMapViewCompositeTool();
                Assert.Equal("actions_inspect_map_view", tool.Id);
                Assert.Equal(ToolKind.Query, tool.Manifest.Kind);

                var res = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "inspect-png-1",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"building_def\":\"Bed\",\"center_x\":12,\"center_z\":12,\"radius\":10}"
                    },
                    CancellationToken.None);

                Assert.True(res.IsOk);
                ToolResult value = res.Value;
                Assert.False(value.IsError);
                Assert.NotNull(value.Attachments);
                Assert.Single(value.Attachments!);

                var img = value.Attachments![0];
                Assert.Equal("image/png", img.MediaType);
                Assert.Equal("high", img.Detail);
                Assert.False(string.IsNullOrWhiteSpace(img.Label));
                Assert.False(string.IsNullOrWhiteSpace(img.Base64Data));

                byte[] decodedPng = Convert.FromBase64String(img.Base64Data);
                DecodeAndVerifyPngRgb24(decodedPng, out int w, out int h);
                Assert.True(w > 200 && h > 200);

                JObject json = JObject.Parse(value.Content);
                Assert.Equal("ok", json["status"]?.ToString());
                Assert.Equal(10, json["view"]?["radius"]?.Value<int>());
                Assert.True(json["zones_summary"]?["growing_cells"]?.Value<int>() > 0);
                Assert.True(json["zones_summary"]?["stockpile_cells"]?.Value<int>() > 0);

                var candidates = json["candidates"] as JArray;
                Assert.NotNull(candidates);
                Assert.InRange(candidates!.Count, 1, 6);
                Assert.Equal(1, candidates[0]?["candidate_id"]?.Value<int>());

                // Verify candidates were cached in VisualCandidateCache
                Assert.True(VisualCandidateCache.TryGetCandidate(1, "Bed", 1, 1000, out var cached));
                Assert.NotNull(cached);
                Assert.Equal(candidates[0]!["x"]!.Value<int>(), cached!.X);
                Assert.Equal(candidates[0]!["z"]!.Value<int>(), cached.Z);

                // Verify ASCII fallback contains farm legend and glyph
                string ascii = json["ascii_fallback"]?.ToString() ?? string.Empty;
                Assert.Contains("G=Farm(NoBuild)", ascii);
                Assert.Contains("G", ascii);

                // Verify unpaired center_x returns InvalidAction
                var badCenter = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "inspect-bad-center",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"center_x\":12}"
                    },
                    CancellationToken.None);
                Assert.True(badCenter.IsErr);
                Assert.Equal(RimMindErrorCode.MechanismInvalidAction, badCenter.Error.Code);
            }
            finally
            {
                VisualCandidateCache.Clear();
            }
        }

        [Fact]
        public void SelectDiverseCandidates_Prefers_Spaced_Candidates_And_Reindexes_1_To_N()
        {
            var raw = new List<SpatialVisualCandidate>
            {
                new SpatialVisualCandidate { X = 10, Z = 10, Score = 100 },
                new SpatialVisualCandidate { X = 11, Z = 10, Score = 99 },  // dist 1 from #1 -> deferred
                new SpatialVisualCandidate { X = 15, Z = 10, Score = 95 },  // dist 5 -> picked #2
                new SpatialVisualCandidate { X = 20, Z = 15, Score = 90 }   // dist 10 -> picked #3
            };

            List<SpatialVisualCandidate> diverse = InspectMapViewCompositeTool.SelectDiverseCandidates(raw, maxCount: 3, minManhattanSpacing: 3);
            Assert.Equal(3, diverse.Count);
            Assert.Equal(1, diverse[0].CandidateId);
            Assert.Equal(10, diverse[0].X);
            Assert.Equal(2, diverse[1].CandidateId);
            Assert.Equal(15, diverse[1].X);
            Assert.Equal(3, diverse[2].CandidateId);
            Assert.Equal(20, diverse[2].X);
        }

        private static byte[] DecodeAndVerifyPngRgb24(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            Assert.True(pngBytes.Length > 33);

            int offset = 8; // skip PNG signature
            bool sawIhdr = false;
            bool sawIend = false;
            var idatStream = new MemoryStream();

            while (offset < pngBytes.Length)
            {
                int length = (int)ReadBigEndianUInt32(pngBytes, offset);
                string type = Encoding.ASCII.GetString(pngBytes, offset + 4, 4);
                int dataStart = offset + 8;
                uint storedCrc = ReadBigEndianUInt32(pngBytes, dataStart + length);
                uint computedCrc = ComputeCrc32(pngBytes, offset + 4, 4 + length);
                Assert.Equal(computedCrc, storedCrc);

                if (type == "IHDR")
                {
                    sawIhdr = true;
                    Assert.Equal(13, length);
                    width = (int)ReadBigEndianUInt32(pngBytes, dataStart);
                    height = (int)ReadBigEndianUInt32(pngBytes, dataStart + 4);
                    Assert.Equal(8, pngBytes[dataStart + 8]); // bit depth
                    Assert.Equal(2, pngBytes[dataStart + 9]); // RGB truecolor
                    Assert.Equal(0, pngBytes[dataStart + 10]); // deflate
                    Assert.Equal(0, pngBytes[dataStart + 11]); // filter
                    Assert.Equal(0, pngBytes[dataStart + 12]); // non-interlaced
                }
                else if (type == "IDAT")
                {
                    idatStream.Write(pngBytes, dataStart, length);
                }
                else if (type == "IEND")
                {
                    sawIend = true;
                    Assert.Equal(0, length);
                    Assert.Equal(0xAE426082u, storedCrc);
                }

                offset = dataStart + length + 4;
            }

            Assert.True(sawIhdr, "Missing IHDR chunk");
            Assert.True(sawIend, "Missing IEND chunk");
            Assert.True(width > 0 && height > 0);

            idatStream.Position = 0;
            byte[] rawScanlines;
            using (var zlib = new ZLibStream(idatStream, CompressionMode.Decompress))
            using (var decompressed = new MemoryStream())
            {
                zlib.CopyTo(decompressed);
                rawScanlines = decompressed.ToArray();
            }

            int stride = width * 3;
            Assert.Equal(height * (1 + stride), rawScanlines.Length);

            byte[] rgb = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * (1 + stride);
                Assert.Equal(0, rawScanlines[srcRow]); // filter None
                Buffer.BlockCopy(rawScanlines, srcRow + 1, rgb, y * stride, stride);
            }

            return rgb;
        }

        private static uint ReadBigEndianUInt32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24)
                 | ((uint)data[offset + 1] << 16)
                 | ((uint)data[offset + 2] << 8)
                 | data[offset + 3];
        }

        private static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1u) != 0 ? 0xEDB88320u ^ (crc >> 1) : (crc >> 1);
                }
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
