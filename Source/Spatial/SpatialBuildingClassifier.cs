namespace RimMind.Actions.Spatial
{
    /// <summary>
    /// Classifies buildings into semantic spatial categories for peer-clustering and room-affinity heuristics.
    /// </summary>
    public static class SpatialBuildingClassifier
    {
        public static SpatialBuildingCategory Classify(
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
    }
}
