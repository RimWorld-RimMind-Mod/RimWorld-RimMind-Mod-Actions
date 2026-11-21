using System;
using System.Linq;
using System.Threading;
using LudeonTK;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.Actions.Debug
{
    /// <summary>
    /// In-game behavioral autotest suite for RimMind-Actions.
    /// Discovered automatically by Core's BehaviorAutotestRunner and exposed to Dev menu.
    /// </summary>
    public sealed class ActionsBehaviorAutotestSuite : BehaviorAutotestSuiteBase
    {
        public override string ModId => "Actions";
        public override string SuiteId => "Behavior.CompositeTools";

        [DebugAction("Autotests", "Run Actions In-Game Behavior Test", actionType = DebugActionType.Action)]
        public static void RunFromDevMenu() => RunSuiteFromDevMenu<ActionsBehaviorAutotestSuite>();

        public override void RunSuite(IInGameBehaviorSuiteContext context)
        {
            var registry = RimMindAPI.ToolSet.Registry;

            // 1. Check Tool Registration
            context.Assert(registry != null, "Tool registry is accessible from RimMindAPI.ToolSet.Registry");
            if (registry == null) return;

            var tool = registry.FindById("actions.stabilize_rest");
            context.Assert(tool != null, "actions.stabilize_rest composite tool is registered in Core registry");

            var fleeTool = registry.FindById("actions.emergency_flee");
            context.Assert(fleeTool != null, "actions.emergency_flee composite tool is registered in Core registry");

            var eatTool = registry.FindById("actions.eat_and_recreation");
            context.Assert(eatTool != null, "actions.eat_and_recreation composite tool is registered in Core registry");

            var triageTool = registry.FindById("actions.triage_patient");
            context.Assert(triageTool != null, "actions.triage_patient composite tool is registered in Core registry");

            if (tool == null) return;

            // 2. Validate Tool Definition Schemas
            context.Assert(tool.Definition.Category == "composite", "Tool definition category is 'composite'");
            context.Assert(fleeTool != null && fleeTool.Definition.Category == "composite", "actions.emergency_flee category is 'composite'");
            context.Assert(eatTool != null && eatTool.Definition.Category == "composite", "actions.eat_and_recreation category is 'composite'");
            context.Assert(triageTool != null && triageTool.Definition.Category == "composite", "actions.triage_patient category is 'composite'");

            // 3. Test In-Game Execution on Colonist (if available)
            Pawn? pawn = context.ActiveColonist;
            if (pawn != null)
            {
                bool wasDrafted = pawn.Drafted;
                try
                {
                    // Call composite action
                    var args = new ToolCallArgs
                    {
                        ToolCallId = "autotest_actions_1",
                        ToolName = "actions.stabilize_rest",
                        ArgumentsJson = $"{{\"pawn_id\":{pawn.thingIDNumber}}}",
                        TraceId = "autotest_actions_trace"
                    };

                    var result = tool.ExecuteAsync(args, CancellationToken.None).GetAwaiter().GetResult();
                    context.Assert(result.IsOk, $"actions.stabilize_rest returned Ok result (Content: {result.Value?.Content})");
                    context.Assert(!pawn.Drafted, "Pawn is confirmed undrafted after stabilize_rest");
                }
                catch (Exception ex)
                {
                    context.Assert(false, $"actions.stabilize_rest execution threw unexpected exception: {ex.Message}");
                }
                finally
                {
                    // Cleanup side effects: cancel forced rest job and restore original draft state
                    try
                    {
                        if (pawn.jobs?.curJob != null && (pawn.jobs.curJob.def == JobDefOf.LayDown || pawn.InBed()))
                        {
                            pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                        }
                        if (pawn.Drafted != wasDrafted && pawn.drafter != null)
                        {
                            pawn.drafter.Drafted = wasDrafted;
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Warn($"Cleanup of pawn resting job encountered warning: {ex.Message}");
                    }
                }
            }
            else
            {
                context.Warn("No colonist available on current map; skipped live entity execution check.");
            }

            // 4. Test Negative Boundary Robustness (invalid pawn_id = -9999)
            try
            {
                var invalidArgs = new ToolCallArgs
                {
                    ToolCallId = "autotest_actions_boundary",
                    ToolName = "actions.stabilize_rest",
                    ArgumentsJson = "{\"pawn_id\":-9999}",
                    TraceId = "autotest_boundary"
                };

                var boundaryResult = tool.ExecuteAsync(invalidArgs, CancellationToken.None).GetAwaiter().GetResult();
                context.Assert(boundaryResult.IsOk || boundaryResult.IsErr, "Negative boundary pawn_id = -9999 handled gracefully without uncaught exception");
            }
            catch (Exception ex)
            {
                context.Assert(false, $"Negative boundary probe threw uncaught exception: {ex.Message}");
            }

            // 5. Spatial Query & Topology (actions_query_buildable_structures)
            try
            {
                var queryBuildableTool = registry.FindById("actions_query_buildable_structures");
                context.Assert(queryBuildableTool != null, "actions_query_buildable_structures is registered in Core registry");
                if (queryBuildableTool != null)
                {
                    var qArgs = new ToolCallArgs
                    {
                        ToolCallId = "autotest_actions_query_buildable",
                        ToolName = "actions_query_buildable_structures",
                        ArgumentsJson = "{\"category\":\"furniture\"}",
                        PawnId = pawn?.thingIDNumber,
                        TraceId = "autotest_actions_query_trace"
                    };

                    var qRes = queryBuildableTool.ExecuteAsync(qArgs, CancellationToken.None).GetAwaiter().GetResult();
                    string qContent = qRes.IsOk ? (qRes.Value?.Content ?? string.Empty) : string.Empty;
                    context.Assert(
                        qRes.IsOk && qRes.Value != null && !qRes.Value.IsError
                        && qContent.Contains("spatial_ascii_map")
                        && qContent.Contains("colony_anchor"),
                        "actions_query_buildable_structures returns spatial_ascii_map and colony_anchor");
                }
            }
            catch (Exception ex)
            {
                context.Assert(false, $"actions_query_buildable_structures check threw unexpected exception: {ex.Message}");
            }

            // 6. Blueprint Permission Gate & Spatial Placement + Work Queue & WorkPriorityModification Gate
            if (pawn != null)
            {
                var placeBlueprintTool = registry.FindById("actions_place_blueprint");
                context.Assert(placeBlueprintTool != null, "actions_place_blueprint is registered in Core registry");
                if (placeBlueprintTool != null)
                {
                    AutonomyLevel origBp = RimMindAPI.Autonomy.GetLevel(pawn, AutonomyDomain.BlueprintPlacement);
                    try
                    {
                        RimMindAPI.Autonomy.SetLevel(pawn, AutonomyDomain.BlueprintPlacement, AutonomyLevel.Disabled);
                        var blockedArgs = new ToolCallArgs
                        {
                            ToolCallId = "autotest_actions_bp_blocked",
                            ToolName = "actions_place_blueprint",
                            ArgumentsJson = $"{{\"pawn_id\":{pawn.thingIDNumber},\"building_def\":\"SleepingSpot\"}}",
                            PawnId = pawn.thingIDNumber,
                            IsPlayerApproved = false,
                            TraceId = "autotest_actions_bp_blocked_trace"
                        };
                        var blockedRes = placeBlueprintTool.ExecuteAsync(blockedArgs, CancellationToken.None).GetAwaiter().GetResult();
                        context.Assert(blockedRes.IsErr, "actions_place_blueprint blocks unpermitted blueprint placement when BlueprintPlacement is Disabled");

                        RimMindAPI.Autonomy.SetLevel(pawn, AutonomyDomain.BlueprintPlacement, AutonomyLevel.FullAutonomous);
                        var allowedArgs = new ToolCallArgs
                        {
                            ToolCallId = "autotest_actions_bp_allowed",
                            ToolName = "actions_place_blueprint",
                            ArgumentsJson = $"{{\"pawn_id\":{pawn.thingIDNumber},\"building_def\":\"SleepingSpot\",\"location_hint\":\"near_base\"}}",
                            PawnId = pawn.thingIDNumber,
                            IsPlayerApproved = false,
                            TraceId = "autotest_actions_bp_allowed_trace"
                        };
                        var allowedRes = placeBlueprintTool.ExecuteAsync(allowedArgs, CancellationToken.None).GetAwaiter().GetResult();
                        context.Assert(allowedRes.IsOk && allowedRes.Value != null && !allowedRes.Value.IsError,
                            $"actions_place_blueprint succeeds when BlueprintPlacement is FullAutonomous ({allowedRes.Value?.Content ?? allowedRes.Error?.Message})");
                    }
                    catch (Exception ex)
                    {
                        context.Assert(false, $"actions_place_blueprint permission gate check threw unexpected exception: {ex.Message}");
                    }
                    finally
                    {
                        RimMindAPI.Autonomy.SetLevel(pawn, AutonomyDomain.BlueprintPlacement, origBp);
                        try
                        {
                            var map = context.CurrentMap ?? pawn.Map;
                            if (map?.listerThings != null)
                            {
                                var cleanupBlueprints = map.listerThings.AllThings
                                    .Where(t => t != null && !t.Destroyed && t.def.IsBlueprint && t.def.entityDefToBuild?.defName == "SleepingSpot")
                                    .ToList();
                                foreach (var bp in cleanupBlueprints)
                                {
                                    bp.Destroy(DestroyMode.Cancel);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            context.Warn($"Cleanup of SleepingSpot blueprints encountered warning: {ex.Message}");
                        }
                    }
                }

                try
                {
                    var jobSetTool = registry.FindById("pawn.job.set");
                    context.Assert(jobSetTool != null, "pawn.job.set is registered in Core registry");
                    if (jobSetTool != null)
                    {
                        var clearArgs = new ToolCallArgs
                        {
                            ToolCallId = "autotest_actions_clear_queue",
                            ToolName = "pawn.job.set",
                            ArgumentsJson = $"{{\"pawn_id\":{pawn.thingIDNumber},\"action\":\"clear_queue\"}}",
                            PawnId = pawn.thingIDNumber,
                            IsPlayerApproved = true,
                            TraceId = "autotest_actions_clear_queue_trace"
                        };
                        var clearRes = jobSetTool.ExecuteAsync(clearArgs, CancellationToken.None).GetAwaiter().GetResult();
                        context.Assert(clearRes.IsOk && clearRes.Value != null && !clearRes.Value.IsError,
                            "pawn.job.set clear_queue executes cleanly on active colonist");
                    }
                }
                catch (Exception ex)
                {
                    context.Assert(false, $"pawn.job.set clear_queue check threw unexpected exception: {ex.Message}");
                }

                var workSetTool = registry.FindById("pawn.work.set");
                context.Assert(workSetTool != null, "pawn.work.set is registered in Core registry");
                if (workSetTool != null)
                {
                    AutonomyLevel origWp = RimMindAPI.Autonomy.GetLevel(pawn, AutonomyDomain.WorkPriorityModification);
                    try
                    {
                        RimMindAPI.Autonomy.SetLevel(pawn, AutonomyDomain.WorkPriorityModification, AutonomyLevel.Disabled);
                        var wpArgs = new ToolCallArgs
                        {
                            ToolCallId = "autotest_actions_wp_blocked",
                            ToolName = "pawn.work.set",
                            ArgumentsJson = $"{{\"pawn_id\":{pawn.thingIDNumber},\"action\":\"set_batch\",\"params\":{{\"priorities\":\"Firefighter=1\"}}}}",
                            PawnId = pawn.thingIDNumber,
                            IsPlayerApproved = false,
                            TraceId = "autotest_actions_wp_blocked_trace"
                        };
                        var wpBlocked = workSetTool.ExecuteAsync(wpArgs, CancellationToken.None).GetAwaiter().GetResult();
                        context.Assert(wpBlocked.IsErr || (wpBlocked.IsOk && wpBlocked.Value != null && wpBlocked.Value.IsError),
                            "pawn.work.set blocks batch priority modification when WorkPriorityModification is Disabled");
                    }
                    catch (Exception ex)
                    {
                        context.Assert(false, $"pawn.work.set permission gate check threw unexpected exception: {ex.Message}");
                    }
                    finally
                    {
                        RimMindAPI.Autonomy.SetLevel(pawn, AutonomyDomain.WorkPriorityModification, origWp);
                    }
                }
            }
        }
    }
}
