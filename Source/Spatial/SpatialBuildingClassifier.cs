using RimMind.Actions.Policies;

namespace RimMind.Actions.Spatial
{
    /// <summary>
    /// Classifies buildings into semantic spatial categories for peer-clustering and room-affinity heuristics,
    /// delegating canonical category matching to <see cref="BuildableStructureCatalogPolicy"/>.
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
            if (key.Contains("shelf") || key.Contains("storage") || key.Contains("hopper"))
                return SpatialBuildingCategory.Storage;

            string category = BuildableStructureCatalogPolicy.ClassifyCategory(
                designationCategoryDefName: null,
                defName: defName,
                isBed: false,
                isWorkTable: false,
                isTable: false,
                isJoy: false,
                isDefense: false);

            return category switch
            {
                "production" => SpatialBuildingCategory.Production,
                "furniture" => SpatialBuildingCategory.DiningRecreation,
                "recreation" => SpatialBuildingCategory.DiningRecreation,
                "temperature" => SpatialBuildingCategory.TemperatureLight,
                "defense" => SpatialBuildingCategory.Defense,
                "structure" => SpatialBuildingCategory.Structure,
                _ => SpatialBuildingCategory.General
            };
        }
    }
}
