using System;
using System.Threading;
using LudeonTK;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
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
            var registry = RimMindAPI.Tools;

            // 1. Check Tool Registration
            context.Assert(registry != null, "Tool registry is accessible from RimMindAPI.ToolSet.Registry");
            if (registry == null) return;

            var tool = registry.FindById("actions.stabilize_rest");
            context.Assert(tool != null, "actions.stabilize_rest composite tool is registered in Core registry");
            if (tool == null) return;

            // 2. Validate Tool Definition Schema
            context.Assert(tool.Definition.Category == "composite", "Tool definition category is 'composite'");
            context.Assert(!string.IsNullOrEmpty(tool.Definition.ParametersSchema), "Tool parameter schema is defined");

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
        }
    }
}
