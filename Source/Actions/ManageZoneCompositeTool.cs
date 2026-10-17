using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.ValueObjects;
using RimWorld;
using Verse;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Composite tool that autonomously creates and configures colony zones
    /// (e.g. fertile Zone_Growing for crops or indoor Zone_Stockpile for storage)
    /// with fertility evaluation, obstacle avoidance, and leader governance.
    /// </summary>
    public sealed class ManageZoneCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_manage_zone";

        public override ToolDefinition Definition => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Autonomously designates or manages colony zones (e.g. creating fertile Zone_Growing for rice/healroot or sheltered Zone_Stockpile) with fertility evaluation and obstacle avoidance.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"zone_type\":{\"type\":\"string\",\"description\":\"Type of zone to create: 'Growing' or 'Stockpile'.\"},\"crop_def\":{\"type\":\"string\",\"description\":\"For growing zones: DefName of the plant to grow (e.g. 'Plant_Rice', 'Plant_Potato', 'Plant_Healroot'). Default: 'Plant_Rice'.\"},\"width\":{\"type\":\"integer\",\"description\":\"Desired width of the zone in tiles (default: 5, range: 2-15).\"},\"height\":{\"type\":\"integer\",\"description\":\"Desired height of the zone in tiles (default: 5, range: 2-15).\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to anchor search location and check autonomy permissions.\"}},\"required\":[\"zone_type\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            if (!TryGetArgument<string>(args.ArgumentsJson, "zone_type", out var zoneTypeStr)
                || string.IsNullOrWhiteSpace(zoneTypeStr))
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, "Missing required parameter 'zone_type' (Growing or Stockpile).")));
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for zone management.")));
            }

            // 1. Resolve Anchor Colonist & Autonomy Permission Guard
            Pawn? pawn = ActionPermissionGuard.ResolveColonistOnMap(map, args, "pawn_id", fallbackToAnyActiveColonist: true);

            var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, AutonomyDomain.ResourceMarking, args);
            if (!permCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
            }

            // 2. Parse Dimensions
            TryGetArgument<int>(args.ArgumentsJson, "width", out var width);
            if (width <= 0) width = 5;
            width = Math.Clamp(width, 2, 15);

            TryGetArgument<int>(args.ArgumentsJson, "height", out var height);
            if (height <= 0) height = 5;
            height = Math.Clamp(height, 2, 15);

            IntVec3 origin = pawn?.Position ?? map.Center;
            string zoneTypeLower = zoneTypeStr.Trim().ToLowerInvariant();
            bool isGrowing = zoneTypeLower.Contains("grow") || zoneTypeLower.Contains("farm") || zoneTypeLower.Contains("plant");

            // 3. Search for Best Rectangular Region
            CellRect bestRect = default;
            int bestValidCells = 0;

            for (int r = 0; r <= 25; r += 2)
            {
                foreach (var offset in GenRadial.RadialCellsAround(origin, r, true))
                {
                    var testRect = new CellRect(offset.x, offset.z, width, height);
                    if (!testRect.FullyContainedWithin(new CellRect(0, 0, map.Size.x, map.Size.z))) continue;

                    int validCells = 0;
                    foreach (var cell in testRect)
                    {
                        if (cell.Fogged(map)) continue;
                        if (map.zoneManager.ZoneAt(cell) != null) continue;

                        var things = map.thingGrid.ThingsListAt(cell);
                        if (things != null && things.Any(t => !t.def.CanOverlapZones)) continue;

                        if (isGrowing)
                        {
                            if (cell.GetFertility(map) < 0.6f) continue;
                        }

                        validCells++;
                    }

                    if (validCells > bestValidCells)
                    {
                        bestValidCells = validCells;
                        bestRect = testRect;
                        if (bestValidCells >= width * height * 0.8f) break;
                    }
                }
                if (bestValidCells >= width * height * 0.8f) break;
            }

            if (bestValidCells < 4)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id,
                        ActionPermissionGuard.TranslateOrFallback(
                            "RimMind.Actions.Zone.NoOpenArea",
                            "[Planning Failed] No contiguous open flat area found nearby (requires >= 4 valid cells)."))));
            }

            // 4. Create and Register Zone
            Zone newZone;
            string details;

            if (isGrowing)
            {
                TryGetArgument<string>(args.ArgumentsJson, "crop_def", out var cropDefName);
                if (string.IsNullOrWhiteSpace(cropDefName)) cropDefName = "Plant_Rice";

                var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail(cropDefName.Trim());
                if (plantDef == null || plantDef.plant == null)
                {
                    plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice")
                               ?? DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.plant != null);
                }

                if (plantDef == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidDefName("No valid growable plant def available.")));
                }

                var growZone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(growZone);

                foreach (var cell in bestRect)
                {
                    if (cell.InBounds(map) && !cell.Fogged(map) && map.zoneManager.ZoneAt(cell) == null)
                    {
                        if (cell.GetFertility(map) >= 0.6f)
                        {
                            var things = map.thingGrid.ThingsListAt(cell);
                            if (things == null || !things.Any(t => !t.def.CanOverlapZones))
                            {
                                growZone.AddCell(cell);
                            }
                        }
                    }
                }

                growZone.SetPlantDefToGrow(plantDef);
                newZone = growZone;
                details = ActionPermissionGuard.TranslateOrFallback(
                    "RimMind.Actions.Zone.GrowZoneDetail",
                    "Growing Zone (crop: {0}, cells: {1})",
                    plantDef.label, growZone.cells.Count);
            }
            else
            {
                var stockpileZone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(stockpileZone);

                foreach (var cell in bestRect)
                {
                    if (cell.InBounds(map) && !cell.Fogged(map) && map.zoneManager.ZoneAt(cell) == null)
                    {
                        var things = map.thingGrid.ThingsListAt(cell);
                        if (things == null || !things.Any(t => !t.def.CanOverlapZones))
                        {
                            stockpileZone.AddCell(cell);
                        }
                    }
                }

                newZone = stockpileZone;
                details = ActionPermissionGuard.TranslateOrFallback(
                    "RimMind.Actions.Zone.StockpileZoneDetail",
                    "Stockpile Zone (cells: {0})",
                    stockpileZone.cells.Count);
            }

            if (newZone.cells.Count == 0)
            {
                newZone.Deregister();
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id,
                        ActionPermissionGuard.TranslateOrFallback(
                            "RimMind.Actions.Zone.NoValidCells",
                            "Failed to designate any valid zone cells."))));
            }

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok(
                    ActionPermissionGuard.TranslateOrFallback(
                        "RimMind.Actions.Zone.DesignateSuccess",
                        "Successfully designated {2} centered at ({0}, {1}).",
                        bestRect.CenterCell.x, bestRect.CenterCell.z, details),
                    args.ToolCallId,
                    Id)));
        }
    }
}
