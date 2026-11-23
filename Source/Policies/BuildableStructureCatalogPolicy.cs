using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Actions.Policies
{
    /// <summary>
    /// Pure material requirement entry extracted from a RimWorld <c>ThingDef.costList</c>.
    /// </summary>
    public readonly struct BuildableMaterialCost
    {
        public BuildableMaterialCost(string defName, string label, int count)
        {
            DefName = defName ?? string.Empty;
            Label = string.IsNullOrWhiteSpace(label) ? DefName : label;
            Count = count;
        }

        public string DefName { get; }
        public string Label { get; }
        public int Count { get; }
    }

    /// <summary>
    /// Pure Verse-independent snapshot of a dynamically discovered player-buildable structure
    /// (covers vanilla buildings AND any modded buildings in <c>DefDatabase&lt;ThingDef&gt;</c>).
    /// </summary>
    public sealed class BuildableStructureDescriptor
    {
        public string DefName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Category { get; set; } = "misc";
        public bool RequiresPower { get; set; }
        public int CostStuffCount { get; set; }
        public string? BestStuffLabel { get; set; }
        public int BestStuffAvailable { get; set; }
        public IReadOnlyList<BuildableMaterialCost> FixedCosts { get; set; } = Array.Empty<BuildableMaterialCost>();
        public string Purpose { get; set; } = string.Empty;
        public int PriorityWeight { get; set; }
    }

    /// <summary>
    /// Projected catalog item serialized back to the LLM.
    /// </summary>
    public sealed class BuildableStructureCatalogItem
    {
        public string DefName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Category { get; set; } = "misc";
        public bool RequiresPower { get; set; }
        public string CostSummary { get; set; } = "Free";
        public bool CanAfford { get; set; }
        public string Purpose { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pure dynamic classification, cost formatting, affordability evaluation, and bounded ranking
    /// for player-buildable structures discovered from RimWorld's <c>DefDatabase&lt;ThingDef&gt;</c>.
    /// </summary>
    public static class BuildableStructureCatalogPolicy
    {
        public const int DefaultMaxResults = 40;

        /// <summary>
        /// Maps RimWorld architect designation categories and building metadata into normalized query categories
        /// (<c>furniture</c>, <c>temperature</c>, <c>production</c>, <c>recreation</c>, <c>defense</c>,
        /// <c>livestock</c>, <c>power</c>, <c>structure</c>, <c>misc</c>).
        /// </summary>
        public static string ClassifyCategory(
            string? designationCategoryDefName,
            string? defName,
            bool isBed = false,
            bool isWorkTable = false,
            bool isTable = false,
            bool isJoy = false,
            bool isDefense = false,
            bool isFenceOrPen = false,
            bool isTempControl = false,
            bool generatesPower = false)
        {
            if (isFenceOrPen)
                return "livestock";

            string des = (designationCategoryDefName ?? string.Empty).Trim();
            if (des.Equals("Furniture", StringComparison.OrdinalIgnoreCase))
                return "furniture";
            if (des.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
                return "temperature";
            if (des.Equals("Production", StringComparison.OrdinalIgnoreCase))
                return "production";
            if (des.Equals("Joy", StringComparison.OrdinalIgnoreCase))
                return "recreation";
            if (des.Equals("Security", StringComparison.OrdinalIgnoreCase))
                return "defense";
            if (des.Equals("Power", StringComparison.OrdinalIgnoreCase))
                return "power";
            if (des.Equals("Structure", StringComparison.OrdinalIgnoreCase))
            {
                if (isDefense) return "defense";
                return "structure";
            }

            if (isBed || isTable) return "furniture";
            if (isWorkTable) return "production";
            if (isJoy) return "recreation";
            if (isDefense) return "defense";
            if (isTempControl) return "temperature";
            if (generatesPower) return "power";

            string key = (defName ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Contains("fence") || key.Contains("penmarker") || key.Contains("animalflap"))
                return "livestock";
            if (key.Contains("bed") || key.Contains("sleep"))
                return "furniture";
            if (key.Contains("stove") || key.Contains("bench") || key.Contains("table_")
                || key.Contains("butcher") || key.Contains("smith") || key.Contains("tailor")
                || key.Contains("research") || key.Contains("machining") || key.Contains("fabrication")
                || key.Contains("brewery") || key.Contains("stonecutter"))
                return "production";
            if (key.Contains("horseshoe") || key.Contains("chess") || key.Contains("hoopstone") || key.Contains("poker"))
                return "recreation";
            if (key.Contains("table") || key.Contains("chair") || key.Contains("stool"))
                return "furniture";
            if (key.Contains("cooler") || key.Contains("heater") || key.Contains("campfire")
                || key.Contains("vent") || key.Contains("torch") || key.Contains("lamp") || key.Contains("brazier"))
                return "temperature";
            if (key.Contains("turret") || key.Contains("trap") || key.Contains("sandbag") || key.Contains("barricade"))
                return "defense";
            if (key.Contains("wall") || key.Contains("door"))
                return "structure";

            return "misc";
        }

        public static string BuildCostSummary(
            int costStuffCount,
            string? stuffLabel,
            IReadOnlyList<BuildableMaterialCost>? fixedCosts)
        {
            var parts = new List<string>();
            if (costStuffCount > 0)
            {
                string mat = string.IsNullOrWhiteSpace(stuffLabel) ? "Stuff" : stuffLabel!.Trim();
                parts.Add($"{costStuffCount} {mat}");
            }

            if (fixedCosts != null)
            {
                for (int i = 0; i < fixedCosts.Count; i++)
                {
                    var cost = fixedCosts[i];
                    if (cost.Count > 0)
                    {
                        parts.Add($"{cost.Count} {cost.Label}");
                    }
                }
            }

            return parts.Count == 0 ? "Free" : string.Join(" + ", parts);
        }

        public static bool CanAfford(
            int costStuffCount,
            int bestStuffAvailable,
            IReadOnlyList<BuildableMaterialCost>? fixedCosts,
            Func<string, int> availableCountLookup)
        {
            if (costStuffCount > 0 && bestStuffAvailable < costStuffCount)
                return false;

            if (fixedCosts != null)
            {
                for (int i = 0; i < fixedCosts.Count; i++)
                {
                    var cost = fixedCosts[i];
                    if (cost.Count <= 0) continue;
                    int have = availableCountLookup != null ? availableCountLookup(cost.DefName) : 0;
                    if (have < cost.Count)
                        return false;
                }
            }

            return true;
        }

        public static string TrimPurpose(string? description, int maxLength = 120)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            string singleLine = description!.Replace("\r", " ").Replace("\n", " ").Trim();
            while (singleLine.Contains("  "))
                singleLine = singleLine.Replace("  ", " ");

            if (singleLine.Length <= maxLength)
                return singleLine;

            int sentenceEnd = singleLine.IndexOf('.', 0, Math.Min(singleLine.Length, maxLength));
            if (sentenceEnd >= 20)
                return singleLine.Substring(0, sentenceEnd + 1);

            return singleLine.Substring(0, maxLength - 3).TrimEnd() + "...";
        }

        public static List<BuildableStructureCatalogItem> FilterAndProject(
            IEnumerable<BuildableStructureDescriptor> candidates,
            string? categoryFilter,
            Func<string, int> availableCountLookup,
            int maxResults = DefaultMaxResults)
        {
            if (candidates == null)
                return new List<BuildableStructureCatalogItem>();

            string filter = string.IsNullOrWhiteSpace(categoryFilter)
                ? "all"
                : categoryFilter!.Trim().ToLowerInvariant();

            bool matchesFilter(BuildableStructureDescriptor d)
            {
                if (filter == "all") return true;
                if (string.Equals(d.Category, filter, StringComparison.OrdinalIgnoreCase)) return true;
                // Keep 'defense' inclusive of walls/doors when explicitly querying defensive structures
                if (filter == "defense" && string.Equals(d.Category, "structure", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }

            int cap = Math.Max(1, maxResults);

            return candidates
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.DefName) && matchesFilter(d))
                .Select(d =>
                {
                    bool affordable = CanAfford(d.CostStuffCount, d.BestStuffAvailable, d.FixedCosts, availableCountLookup);
                    return new
                    {
                        Descriptor = d,
                        Affordable = affordable,
                        CostSummary = BuildCostSummary(d.CostStuffCount, d.BestStuffLabel, d.FixedCosts)
                    };
                })
                .OrderByDescending(x => x.Affordable)
                .ThenByDescending(x => x.Descriptor.PriorityWeight)
                .ThenBy(x => x.Descriptor.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Descriptor.DefName, StringComparer.OrdinalIgnoreCase)
                .Take(cap)
                .Select(x => new BuildableStructureCatalogItem
                {
                    DefName = x.Descriptor.DefName,
                    Label = string.IsNullOrWhiteSpace(x.Descriptor.Label) ? x.Descriptor.DefName : x.Descriptor.Label,
                    Category = x.Descriptor.Category,
                    RequiresPower = x.Descriptor.RequiresPower,
                    CostSummary = x.CostSummary,
                    CanAfford = x.Affordable,
                    Purpose = TrimPurpose(x.Descriptor.Purpose)
                })
                .ToList();
        }
    }
}
