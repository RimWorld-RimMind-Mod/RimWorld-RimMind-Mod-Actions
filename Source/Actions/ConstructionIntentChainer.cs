#if !RIMMIND_ACTIONS_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Shared construction intent-lease chainer and RimWorld ThingDef/StuffDef lookup helper
    /// used by both <see cref="PlaceBlueprintCompositeTool"/> and <see cref="PlanRoomProjectCompositeTool"/>.
    /// Respects player work priorities (never calls <c>workSettings.SetPriority</c>).
    /// </summary>
    internal static class ConstructionIntentChainer
    {
        public static ThingDef? ResolveThingDef(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return null;

            string trimmed = rawName!.Trim();
            var exact = DefDatabase<ThingDef>.GetNamedSilentFail(trimmed);
            if (exact != null)
                return exact;

            return DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d != null
                                  && (string.Equals(d.defName, trimmed, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(d.label, trimmed, StringComparison.OrdinalIgnoreCase)));
        }

        public static ThingDef? ResolveStuffForBuilding(ThingDef buildingDef, string? requestedStuffName, Map map)
        {
            if (buildingDef == null || !buildingDef.MadeFromStuff)
                return null;

            ThingDef? selectedStuff = ResolveThingDef(requestedStuffName);
            if (selectedStuff != null && !GenStuff.AllowedStuffsFor(buildingDef).Contains(selectedStuff))
            {
                selectedStuff = null;
            }

            if (selectedStuff != null)
                return selectedStuff;

            var validStuffs = GenStuff.AllowedStuffsFor(buildingDef).ToList();
            selectedStuff = validStuffs
                .OrderByDescending(s => MapResourceCounter.GetAvailableCount(map, s))
                .FirstOrDefault();

            if (selectedStuff == null || MapResourceCounter.GetAvailableCount(map, selectedStuff) <= 0)
            {
                selectedStuff = GenStuff.DefaultStuffFor(buildingDef) ?? ThingDefOf.WoodLog ?? ThingDefOf.Steel;
            }

            return selectedStuff;
        }

        public static bool TryChainSingleBlueprint(
            Pawn? builderPawn,
            Map? map,
            Blueprint_Build? blueprint,
            IntVec3 targetCell,
            string intentId,
            int durationTicks = 2500,
            float priority = 7.8f)
        {
            if (builderPawn == null || map == null || blueprint == null)
                return false;

            var blueprints = new List<Blueprint> { blueprint };
            var targetCells = new List<IntVec3> { targetCell };
            return TryChainProject(builderPawn, map, blueprints, targetCells, intentId, durationTicks, priority);
        }

        public static bool TryChainProject(
            Pawn? builderPawn,
            Map? map,
            IReadOnlyList<Blueprint> blueprints,
            IReadOnlyList<IntVec3> targetCells,
            string intentId,
            int durationTicks = 5000,
            float priority = 7.8f)
        {
            if (builderPawn == null || map == null || blueprints == null || blueprints.Count == 0)
                return false;

            Blueprint firstTarget = blueprints[0];
            int startTick = Find.TickManager?.TicksGame ?? 0;

            return RimMindAPI.Agent.StartIntentSession(
                pawn: builderPawn,
                intentId: intentId,
                startTick: startTick,
                durationTicks: durationTicks,
                priority: priority,
                target: firstTarget,
                jobProvider: p => IssueConstructionSubJob(p, map, blueprints, targetCells),
                completionPredicate: _ => IsProjectCompleted(map, blueprints, targetCells));
        }

        public static Job? IssueConstructionSubJob(
            Pawn? pawn,
            Map? map,
            IReadOnlyList<Blueprint> blueprints,
            IReadOnlyList<IntVec3> targetCells)
        {
            if (pawn == null || map == null)
                return null;

            // Respect the player's work tab: never rewrite priorities.
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                || (pawn.workSettings != null && !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction)))
            {
                return null;
            }

            try
            {
                var deliverBpGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints")?.Worker as WorkGiver_Scanner
                    ?? new WorkGiver_ConstructDeliverResourcesToBlueprints();
                var deliverFrameGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToFrames")?.Worker as WorkGiver_Scanner
                    ?? new WorkGiver_ConstructDeliverResourcesToFrames();
                var finishGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames")?.Worker as WorkGiver_Scanner
                    ?? new WorkGiver_ConstructFinishFrames();

                for (int i = 0; i < blueprints.Count; i++)
                {
                    var bp = blueprints[i];
                    if (bp == null || bp.Destroyed || !bp.Spawned) continue;
                    if (!pawn.CanReserveAndReach(bp, PathEndMode.Touch, Danger.Some)) continue;

                    var haulJob = deliverBpGiver.JobOnThing(pawn, bp, true);
                    if (haulJob != null) return haulJob;

                    var finishBpJob = finishGiver.JobOnThing(pawn, bp, true);
                    if (finishBpJob != null) return finishBpJob;
                }

                for (int i = 0; i < targetCells.Count; i++)
                {
                    var cell = targetCells[i];
                    if (!cell.IsValid || !cell.InBounds(map)) continue;

                    var frame = cell.GetThingList(map).OfType<Frame>().FirstOrDefault(f => !f.Destroyed && f.Spawned);
                    if (frame == null || !pawn.CanReserveAndReach(frame, PathEndMode.Touch, Danger.Some)) continue;

                    var deliverJob = deliverFrameGiver.JobOnThing(pawn, frame, true);
                    if (deliverJob != null) return deliverJob;

                    var finishJob = finishGiver.JobOnThing(pawn, frame, true);
                    if (finishJob != null) return finishJob;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static bool IsProjectCompleted(
            Map? map,
            IReadOnlyList<Blueprint> blueprints,
            IReadOnlyList<IntVec3> targetCells)
        {
            if (map == null)
                return true;

            for (int i = 0; i < blueprints.Count; i++)
            {
                var bp = blueprints[i];
                if (bp != null && !bp.Destroyed && bp.Spawned)
                    return false;
            }

            for (int i = 0; i < targetCells.Count; i++)
            {
                var cell = targetCells[i];
                if (cell.IsValid && cell.InBounds(map)
                    && cell.GetThingList(map).OfType<Frame>().Any(f => !f.Destroyed && f.Spawned))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
