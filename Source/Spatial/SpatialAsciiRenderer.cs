using System;
using System.Collections.Generic;
using System.Text;

namespace RimMind.Actions.Spatial
{
    /// <summary>
    /// Renders a compact ASCII top-down map of a <see cref="ColonySpatialSnapshot"/> for LLM prompts and diagnostics.
    /// </summary>
    public static class SpatialAsciiRenderer
    {
        public static string RenderCompactAsciiGrid(
            ColonySpatialSnapshot? snapshot,
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
