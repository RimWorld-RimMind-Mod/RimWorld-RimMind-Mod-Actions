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
    /// Composite tool that autonomously adds production bills to colony worktables
    /// (e.g. cooking simple meals at stoves, butchering creatures, tailoring parkas)
    /// with duplicate prevention, target count safeguards, and leader permission governance.
    /// </summary>
    public sealed class AddWorktableBillCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions_add_worktable_bill";

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Adds a production bill (e.g. CookMealSimple, ButcherCorpseFlesh, Make_Apparel_Parka) to an available worktable in the colony with target count safety limits and duplicate prevention.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"recipe_def\":{\"type\":\"string\",\"description\":\"DefName of the recipe to add (e.g. 'CookMealSimple', 'CookMealFine', 'Make_Apparel_Parka', 'ButcherCorpseFlesh').\"},\"worktable_def\":{\"type\":\"string\",\"description\":\"Optional DefName of the target worktable (e.g. 'TableStoveElectric', 'TableStoveFueled', 'HandTailoringBench', 'TableButcher'). If omitted, searches for any capable player worktable.\"},\"target_count\":{\"type\":\"integer\",\"description\":\"Target inventory stock count or repeat count (default: 10).\"},\"repeat_mode\":{\"type\":\"string\",\"description\":\"Production mode: 'TargetCount' (make until you have X, default) or 'RepeatCount' (make X times).\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to assign exclusively or anchor permission check.\"}},\"required\":[\"recipe_def\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            if (!TryGetArgument<string>(args.ArgumentsJson, "recipe_def", out var recipeDefName)
                || string.IsNullOrWhiteSpace(recipeDefName))
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, "Missing required parameter 'recipe_def'.")));
            }

            var actorResolution = ActionPermissionGuard.ResolveActorAndMap(args, "pawn_id", fallbackToAnyActiveColonist: true);
            if (actorResolution.Error != null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, actorResolution.Error)));
            }

            var map = actorResolution.Map;
            if (map == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.Internal("No active map available for adding worktable bill.")));
            }

            // 1. Resolve Assigned Colonist Pawn
            Pawn? pawn = actorResolution.Pawn;

            // 2. Resolve RecipeDef
            var recipeDef = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName.Trim());
            if (recipeDef == null)
            {
                recipeDef = DefDatabase<RecipeDef>.AllDefsListForReading
                    .FirstOrDefault(r => r.defName.Equals(recipeDefName.Trim(), StringComparison.OrdinalIgnoreCase)
                                      || r.label.Equals(recipeDefName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (recipeDef == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidDefName($"Recipe '{recipeDefName}' not found in database.")));
            }

            var billAutonomyDomain = IsEssentialSurvivalRecipe(recipeDef.defName)
                ? AutonomyDomain.EmergencySurvival
                : AutonomyDomain.WorktableBills;
            var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, billAutonomyDomain, args);
            if (!permCheck.IsOk)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
            }

            // 3. Resolve Target WorkTable
            TryGetArgument<string>(args.ArgumentsJson, "worktable_def", out var worktableDefName);
            Building_WorkTable? targetWorktable = null;

            var playerWorktables = map.listerBuildings?.allBuildingsColonist
                .OfType<Building_WorkTable>()
                .Where(w => !w.Destroyed && w.Spawned)
                .ToList() ?? new List<Building_WorkTable>();

            if (!string.IsNullOrWhiteSpace(worktableDefName))
            {
                targetWorktable = playerWorktables.FirstOrDefault(w =>
                    w.def.defName.Equals(worktableDefName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (targetWorktable == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, $"Specified worktable '{worktableDefName}' not found or destroyed in player colony.")));
                }
            }
            else
            {
                // Find any table that can execute this recipe
                targetWorktable = playerWorktables.FirstOrDefault(w =>
                    (w.def.recipes != null && w.def.recipes.Contains(recipeDef))
                    || (w.def.AllRecipes != null && w.def.AllRecipes.Contains(recipeDef)));
            }

            if (targetWorktable == null)
            {
                return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                    RimMindErrors.InvalidAction(Id, $"No suitable worktable found in colony capable of executing recipe '{recipeDef.defName}'.")));
            }

            // 4. Ingredient & Duplicate Check
            bool isCookingRecipe = recipeDef.defName.Contains("Meal") || recipeDef.defName.Contains("Cook");
            int rawNutrition = -1;
            if (isCookingRecipe && targetWorktable.Map != null)
            {
                var rawCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodRaw");
                rawNutrition = (rawCat != null && targetWorktable.Map.resourceCounter != null)
                    ? targetWorktable.Map.resourceCounter.GetCountIn(rawCat)
                    : 0;
                if (rawNutrition <= 0 && rawCat != null && targetWorktable.Map.listerThings != null)
                {
                    var mapRawFoods = targetWorktable.Map.listerThings
                        .ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                        .Where(t => t != null && t.Spawned && t.def?.thingCategories != null && t.def.thingCategories.Contains(rawCat))
                        // Forbid state is a player decision: count only accessible food, never unforbid it.
                        .Where(t => !t.IsForbidden(Faction.OfPlayer))
                        .ToList();
                    rawNutrition = mapRawFoods.Sum(t => t.stackCount);
                }
                if (rawNutrition <= 0)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(
                            Id,
                            $"Missing raw food ingredients (0 raw food on map) for '{recipeDef.defName}' on '{targetWorktable.LabelCap}'. Call actions_designate_resources (Harvest) to gather wild food first.")));
                }
            }

            if (targetWorktable.billStack != null)
            {
                var existingBill = targetWorktable.billStack.Bills
                    .OfType<Bill_Production>()
                    .FirstOrDefault(b => b.recipe == recipeDef && !b.suspended);

                if (existingBill != null)
                {
                    int currentTarget = existingBill.repeatMode == BillRepeatModeDefOf.TargetCount
                        ? existingBill.targetCount
                        : existingBill.repeatCount;

                    var refuelComp = targetWorktable.GetComp<CompRefuelable>();
                    string fuelHint = (refuelComp != null && !refuelComp.HasFuel)
                        ? $" Note: '{targetWorktable.LabelCap}' has 0 fuel (WoodLog) — call designate_map_order(Harvest, Tree) or haul wood instead of adding bills."
                        : " Choose a different task or assign work directly.";

                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(
                            Id,
                            $"Active bill for '{recipeDef.label}' already exists on '{targetWorktable.LabelCap}' (target: {currentTarget}).{fuelHint}")));
                }
            }

            // 5. Parse Options and Create Bill
            TryGetArgument<int>(args.ArgumentsJson, "target_count", out var targetCount);
            if (targetCount <= 0) targetCount = 10;

            TryGetArgument<string>(args.ArgumentsJson, "repeat_mode", out var repeatModeStr);
            bool isRepeatCount = string.Equals(repeatModeStr, "RepeatCount", StringComparison.OrdinalIgnoreCase);

            var newBill = recipeDef.MakeNewBill() as Bill_Production ?? new Bill_Production(recipeDef);
            if (isRepeatCount)
            {
                newBill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                newBill.repeatCount = targetCount;
            }
            else
            {
                newBill.repeatMode = BillRepeatModeDefOf.TargetCount;
                newBill.targetCount = targetCount;
            }

            if (pawn != null)
            {
                newBill.SetPawnRestriction(pawn);
            }

            targetWorktable.billStack?.AddBill(newBill);

            string assignedNotice = pawn != null ? $" (assigned: {pawn.LabelShort})" : "";
            string modeDesc = isRepeatCount ? $"repeat {targetCount}x" : $"target count: {targetCount}";

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                ToolResult.Ok(
                    ActionPermissionGuard.TranslateOrFallback(
                        "RimMind.Actions.Bill.AddSuccess",
                        "Successfully added bill '{0}' on '{1}'{2} ({3}).",
                        recipeDef.label,
                        targetWorktable.LabelCap,
                        assignedNotice,
                        modeDesc),
                    args.ToolCallId,
                    Id)));
        }

        private static bool IsEssentialSurvivalRecipe(string? recipeDefName)
        {
            if (string.IsNullOrWhiteSpace(recipeDefName)) return false;
            string r = recipeDefName!.Trim();
            return r.StartsWith("CookMeal", StringComparison.OrdinalIgnoreCase)
                || r.Equals("Make_Pemmican", StringComparison.OrdinalIgnoreCase)
                || r.StartsWith("Butcher", StringComparison.OrdinalIgnoreCase)
                || r.Equals("Make_Kibble", StringComparison.OrdinalIgnoreCase)
                || r.Equals("Make_Apparel_Parka", StringComparison.OrdinalIgnoreCase)
                || r.Equals("Make_Apparel_Tuque", StringComparison.OrdinalIgnoreCase)
                || r.StartsWith("Make_StoneBlocks", StringComparison.OrdinalIgnoreCase);
        }
    }
}
