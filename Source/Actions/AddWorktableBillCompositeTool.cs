using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Policies;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
#if !RIMMIND_ACTIONS_TESTS
using RimWorld;
#endif
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

        private static readonly string[] Aliases = new[]
        {
            "add_worktable_bill",
            "actions.add_worktable_bill"
        };

        private static readonly string[] SummaryKeys = new[]
        {
            "recipe_def",
            "worktable_def"
        };

        protected override AutonomyDomain? DefaultAutonomyDomain => AutonomyDomain.WorktableBills;
        protected override ToolScope? ToolScopes => ToolScope.ColonyBuild;
        protected override IReadOnlyList<string> ToolAliases => Aliases;
        protected override bool IsSelfTargetedTool => false;
        protected override string? DisplayName => "Add Worktable Bill";
        protected override IReadOnlyList<string> SummaryArgKeys => SummaryKeys;

        protected override ToolDefinition BuildDefinition() => new ToolDefinition
        {
            Id = Id,
            Category = "action",
            Description = "Adds a production bill (e.g. CookMealSimple, ButcherCorpseFlesh, Make_Apparel_Parka) to an available worktable in the colony with target count safety limits and duplicate prevention.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"recipe_def\":{\"type\":\"string\",\"description\":\"DefName of the recipe to add (e.g. 'CookMealSimple', 'CookMealFine', 'Make_Apparel_Parka', 'ButcherCorpseFlesh').\"},\"worktable_def\":{\"type\":\"string\",\"description\":\"Optional DefName of the target worktable (e.g. 'FueledStove', 'ElectricStove', 'Campfire', 'HandTailoringBench', 'TableButcher'). If omitted or not present, searches for any capable player worktable.\"},\"target_count\":{\"type\":\"integer\",\"description\":\"Target inventory stock count or repeat count (default: 10).\"},\"repeat_mode\":{\"type\":\"string\",\"description\":\"Production mode: 'TargetCount' (make until you have X, default) or 'RepeatCount' (make X times).\"},\"pawn_id\":{\"type\":\"integer\",\"description\":\"Optional colonist pawn ID to assign exclusively or anchor permission check.\"}},\"required\":[\"recipe_def\"]}"
        };

        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();

        protected override AutonomyDomain? ResolveAutonomyDomain(JObject? root, ToolCallArgs args)
        {
            TryGetArgument<string>(root, "recipe_def", out var recipeDefName);
            string? trimmed = recipeDefName?.Trim();
#if !RIMMIND_ACTIONS_TESTS
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                var def = DefDatabase<RecipeDef>.GetNamedSilentFail(trimmed);
                if (def != null)
                {
                    return IsEssentialSurvivalRecipe(def)
                        ? AutonomyDomain.EmergencySurvival
                        : AutonomyDomain.WorktableBills;
                }
            }
#endif
            return IsEssentialSurvivalRecipeName(trimmed)
                ? AutonomyDomain.EmergencySurvival
                : AutonomyDomain.WorktableBills;
        }

        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
            ToolCallArgs args,
            CancellationToken ct)
        {
            return ExecuteDirectToolAsync(args, (argsRoot, effectiveArgs, _) =>
            {
                if (!TryGetArgument<string>(argsRoot, "recipe_def", out var recipeDefName)
                    || string.IsNullOrWhiteSpace(recipeDefName))
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, "Missing required parameter 'recipe_def'.") with { TraceId = effectiveArgs.TraceId }));
                }

#if RIMMIND_ACTIONS_TESTS
                var domain = ResolveAutonomyDomain(argsRoot, effectiveArgs)
                    ?? AutonomyDomain.WorktableBills;
                if (TryGetArgument<int>(argsRoot, "pawn_id", out int testPawnId) && testPawnId > 0)
                {
                    var guard = GuardColonistTarget(testPawnId, domain, effectiveArgs);
                    if (guard.IsErr)
                    {
                        return Task.FromResult(Result<ToolResult, RimMindError>.Err(guard.Error));
                    }
                }
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(
                    ToolResult.Ok($"Added bill {recipeDefName}", effectiveArgs.ToolCallId, Id)));
#else
                bool hasExplicitPawnId = TryGetArgument<int>(argsRoot, "pawn_id", out int explicitPawnId) && explicitPawnId > 0;
                var actorResolution = ActionPermissionGuard.ResolveActorAndMap(effectiveArgs, "pawn_id", fallbackToAnyActiveColonist: true, parsedArgs: argsRoot);
                if (actorResolution.Error != null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, actorResolution.Error) with { TraceId = effectiveArgs.TraceId }));
                }

                var map = actorResolution.Map;
                if (map == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.Internal("No active map available for adding worktable bill.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 1. Resolve Assigned Colonist Pawn
                Pawn? pawn = actorResolution.Pawn;

                // 2. Resolve RecipeDef
                var trimmedRecipeName = recipeDefName.Trim();
                var recipeDef = DefDatabase<RecipeDef>.GetNamedSilentFail(trimmedRecipeName);
                if (recipeDef == null)
                {
                    recipeDef = DefDatabase<RecipeDef>.AllDefsListForReading
                        .FirstOrDefault(r => r != null
                                          && (string.Equals(r.defName, trimmedRecipeName, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(r.label, trimmedRecipeName, StringComparison.OrdinalIgnoreCase)));
                }

                if (recipeDef == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidDefName($"Recipe '{recipeDefName}' not found in database.") with { TraceId = effectiveArgs.TraceId }));
                }

                var billAutonomyDomain = IsEssentialSurvivalRecipe(recipeDef)
                    ? AutonomyDomain.EmergencySurvival
                    : AutonomyDomain.WorktableBills;
                var permCheck = ActionPermissionGuard.CheckPermission(pawn, Id, billAutonomyDomain, effectiveArgs);
                if (!permCheck.IsOk)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(permCheck.Error));
                }

                // 3. Resolve Target WorkTable
                TryGetArgument<string>(argsRoot, "worktable_def", out var worktableDefName);
                Building_WorkTable? targetWorktable = null;

                var playerWorktables = map.listerBuildings?.allBuildingsColonist
                    .OfType<Building_WorkTable>()
                    .Where(w => !w.Destroyed && w.Spawned)
                    .ToList() ?? new List<Building_WorkTable>();

                if (!string.IsNullOrWhiteSpace(worktableDefName))
                {
                    var trimmedTableName = worktableDefName.Trim();
                    targetWorktable = playerWorktables.FirstOrDefault(w =>
                        w?.def != null
                        && (string.Equals(w.def.defName, trimmedTableName, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(w.def.label, trimmedTableName, StringComparison.OrdinalIgnoreCase)));

                    if (targetWorktable == null)
                    {
                        targetWorktable = FindCapableWorktable(playerWorktables, recipeDef);
                        if (targetWorktable == null)
                        {
                            string available = playerWorktables.Count > 0
                                ? string.Join(", ", playerWorktables.Select(w => w.def.defName).Distinct())
                                : "none";
                            return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                                RimMindErrors.InvalidAction(Id, $"Specified worktable '{worktableDefName}' not found in player colony and no other worktable can run '{recipeDef.defName}'. Available worktables: {available}.") with { TraceId = effectiveArgs.TraceId }));
                        }
                    }
                }
                else
                {
                    targetWorktable = FindCapableWorktable(playerWorktables, recipeDef);
                }

                if (targetWorktable == null)
                {
                    return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                        RimMindErrors.InvalidAction(Id, $"No suitable worktable found in colony capable of executing recipe '{recipeDef.defName}'.") with { TraceId = effectiveArgs.TraceId }));
                }

                // 4. Ingredient & Duplicate Check
                bool isCookingRecipe = IsCookingRecipe(recipeDef);
                if (isCookingRecipe && targetWorktable.Map != null)
                {
                    var rawCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodRaw");
                    int rawNutrition = (rawCat != null && targetWorktable.Map.resourceCounter != null)
                        ? targetWorktable.Map.resourceCounter.GetCountIn(rawCat)
                        : 0;
                    if (rawNutrition <= 0 && rawCat != null && targetWorktable.Map.listerThings != null)
                    {
                        var mapRawFoods = targetWorktable.Map.listerThings
                            .ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                            .Where(t => t != null
                                     && t.Spawned
                                     && !t.Destroyed
                                     && !t.Position.Fogged(targetWorktable.Map)
                                     && t.def?.thingCategories != null
                                     && t.def.thingCategories.Contains(rawCat)
                                     && !t.IsForbidden(Faction.OfPlayer))
                            .ToList();
                        rawNutrition = mapRawFoods.Sum(t => t.stackCount);
                    }
                    if (rawNutrition <= 0)
                    {
                        return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                            RimMindErrors.InvalidAction(
                                Id,
                                $"Missing raw food ingredients (0 raw food on map) for '{recipeDef.defName}' on '{targetWorktable.LabelCap}'. Call actions_designate_resources with designation_type 'Hunt' (wild animals) or 'Harvest' (wild plants/crops) to gather food first.") with { TraceId = effectiveArgs.TraceId }));
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
                            ? $" Note: '{targetWorktable.LabelCap}' has 0 fuel (WoodLog) — call actions_designate_resources with designation_type 'ChopWood' or haul wood instead of adding bills."
                            : " Choose a different task or assign work directly.";

                        return Task.FromResult(Result<ToolResult, RimMindError>.Err(
                            RimMindErrors.InvalidAction(
                                Id,
                                $"Active bill for '{recipeDef.label}' already exists on '{targetWorktable.LabelCap}' (target: {currentTarget}).{fuelHint}") with { TraceId = effectiveArgs.TraceId }));
                    }
                }

                // 5. Parse Options and Create Bill
                TryGetArgument<int>(argsRoot, "target_count", out var targetCount);
                if (targetCount <= 0) targetCount = 10;

                TryGetArgument<string>(argsRoot, "repeat_mode", out var repeatModeStr);
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

                // Only restrict the bill to a single colonist when explicitly requested via pawn_id
                // and that colonist is actually allowed to do the recipe's work type.
                Pawn? restrictedPawn = null;
                if (hasExplicitPawnId && pawn != null && CanPawnPerformRecipe(pawn, recipeDef))
                {
                    newBill.SetPawnRestriction(pawn);
                    restrictedPawn = pawn;
                }

                targetWorktable.billStack?.AddBill(newBill);

                string assignedNotice = restrictedPawn != null ? $" (assigned: {restrictedPawn.LabelShort})" : string.Empty;
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
                        effectiveArgs.ToolCallId,
                        Id)));
#endif
            }, ct);
        }

#if !RIMMIND_ACTIONS_TESTS
        private static Building_WorkTable? FindCapableWorktable(List<Building_WorkTable> tables, RecipeDef recipeDef)
        {
            return tables.FirstOrDefault(w =>
                (w.def.recipes != null && w.def.recipes.Contains(recipeDef))
                || (w.def.AllRecipes != null && w.def.AllRecipes.Contains(recipeDef)));
        }

        private static bool CanPawnPerformRecipe(Pawn pawn, RecipeDef recipeDef)
        {
            if (recipeDef.workSkill != null && pawn.skills?.GetSkill(recipeDef.workSkill)?.TotallyDisabled == true)
                return false;
            return true;
        }

        private static bool IsCookingRecipe(RecipeDef recipeDef)
        {
            if (recipeDef.products != null)
            {
                for (int i = 0; i < recipeDef.products.Count; i++)
                {
                    var prod = recipeDef.products[i]?.thingDef;
                    if (prod != null && prod.IsNutritionGivingIngestible && prod.ingestible?.HumanEdible == true)
                        return true;
                }
            }
            return (recipeDef.defName?.IndexOf("Meal", StringComparison.OrdinalIgnoreCase) >= 0)
                || (recipeDef.defName?.IndexOf("Cook", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsEssentialSurvivalRecipe(RecipeDef recipeDef)
        {
            if (recipeDef == null) return false;
            if (recipeDef.products != null)
            {
                for (int i = 0; i < recipeDef.products.Count; i++)
                {
                    var prod = recipeDef.products[i]?.thingDef;
                    if (prod == null) continue;
                    // Any human-edible food/meal/pemmican or animal feed (kibble)
                    if (prod.IsNutritionGivingIngestible)
                        return true;
                    // Protective thermal apparel (cold or heat insulation)
                    if (prod.IsApparel)
                    {
                        float coldIns = prod.GetStatValueAbstract(StatDefOf.Insulation_Cold);
                        float heatIns = prod.GetStatValueAbstract(StatDefOf.Insulation_Heat);
                        if (coldIns >= 12f || heatIns >= 12f)
                            return true;
                    }
                    // Structural stone blocks
                    if (prod.stuffProps?.categories != null && prod.stuffProps.categories.Contains(StuffCategoryDefOf.Stony))
                        return true;
                }
            }

            // Butchering recipes produce meat from corpses via specialProducts rather than static products
            if (recipeDef.specialProducts != null && recipeDef.specialProducts.Contains(SpecialProductType.Butchery))
                return true;

            return IsEssentialSurvivalRecipeName(recipeDef.defName);
        }
#endif

        private static bool IsEssentialSurvivalRecipeName(string? recipeDefName)
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
