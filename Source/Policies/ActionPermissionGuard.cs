using RimMind.Actions.Actions;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Actions.Policies
{
    /// <summary>
    /// Guard that enforces colonist tri-state autonomy permissions before executing autonomous composite tools.
    /// </summary>
    public static class ActionPermissionGuard
    {
        public static bool IsChineseLanguageActive()
        {
            try
            {
                string lang = LanguageDatabase.activeLanguage?.folderName ?? LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "";
                return lang.IndexOf("Chinese", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        public static string TranslateOrFallback(string key, string fallbackEn, params object[] args)
            => ResourceSafetyPolicy.TranslateOrFallback(key, fallbackEn, args);

        public static Result<Unit, RimMindError> CheckPermission(
            Pawn? pawn,
            string toolId,
            AutonomyDomain domain,
            bool isPreApproved = false)
        {
            var level = RimMindAPI.Autonomy.GetLevel(pawn, domain);

            if (level == AutonomyLevel.Disabled)
            {
                return Result<Unit, RimMindError>.Err(
                    RimMindErrors.InvalidAction(toolId,
                        TranslateOrFallback(
                            "RimMind.Actions.Permission.Disabled",
                            "[Permission Blocked] Colony leader has disabled autonomous decisions in domain ({0}) for this colonist.",
                            domain)));
            }

            if (level == AutonomyLevel.RequireApproval)
            {
                if (isPreApproved)
                {
                    return Result<Unit, RimMindError>.Ok(Unit.Value);
                }

                return Result<Unit, RimMindError>.Err(
                    RimMindErrors.InvalidAction(toolId,
                        TranslateOrFallback(
                            "RimMind.Actions.Permission.RequireApproval",
                            "[Approval Required] This operation ({0}) requires colony leader approval before execution.",
                            domain)));
            }

            if (level == AutonomyLevel.FullAutonomous)
            {
                return Result<Unit, RimMindError>.Ok(Unit.Value);
            }

            return Result<Unit, RimMindError>.Ok(Unit.Value);
        }

        public static Result<Unit, RimMindError> CheckPermission(
            Pawn? pawn,
            string toolId,
            AutonomyDomain domain,
            ToolCallArgs args)
        {
            bool preApproved = CompositeToolCallBase.IsPreApproved(args);
            return CheckPermission(pawn, toolId, domain, preApproved);
        }

        /// <summary>
        /// Result of <see cref="ResolveActorAndMap"/>: the acting colonist (may be null when no id was supplied
        /// and no fallback colonist exists), the map the action must run on, and an error when a pawn id was
        /// supplied but does not refer to an active, standing colonist.
        /// </summary>
        public readonly struct ActorResolution
        {
            public ActorResolution(Pawn? pawn, Map? map, string? error)
            {
                Pawn = pawn;
                Map = map;
                Error = error;
            }

            public Pawn? Pawn { get; }
            public Map? Map { get; }
            public string? Error { get; }
        }

        /// <summary>
        /// Resolves the acting colonist across all maps and returns <em>that colonist's</em> map, so actions never
        /// land on whatever map the player happens to be viewing. Lookup order: <paramref name="pawnIdKey"/>
        /// argument, <see cref="ToolCallArgs.PawnId"/>, <see cref="ToolCallArgs.NpcId"/>.
        /// If any id is supplied but none resolves to an active standing colonist, an error is returned instead
        /// of silently substituting another colonist. Only when no id is supplied at all does it fall back to
        /// <see cref="Find.CurrentMap"/> (and optionally any active colonist on it).
        /// </summary>
        public static ActorResolution ResolveActorAndMap(
            ToolCallArgs args,
            string pawnIdKey = "pawn_id",
            bool fallbackToAnyActiveColonist = false)
        {
            bool anyIdSupplied = false;

            Pawn? TryResolve(int id)
            {
                if (id <= 0) return null;
                anyIdSupplied = true;
                var p = RimMindPawnLookup.FindPawnByNumber(id);
                return p != null && RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed ? p : null;
            }

            Pawn? actor = null;
            if (CompositeToolCallBase.TryGetArgument<int>(args.ArgumentsJson, pawnIdKey, out int argPawnId))
                actor = TryResolve(argPawnId);
            if (actor == null && args.PawnId.HasValue)
                actor = TryResolve(args.PawnId.Value);
            if (actor == null && CompositeToolCallBase.TryParsePawnIdString(args.NpcId, out int npcPawnId))
                actor = TryResolve(npcPawnId);

            if (actor != null)
                return new ActorResolution(actor, actor.Map, null);

            if (anyIdSupplied)
            {
                return new ActorResolution(null, null,
                    TranslateOrFallback(
                        "RimMind.Actions.Permission.InvalidColonist",
                        "Requested colonist is not an active, standing colonist on any map (dead, downed, off-map, prisoner or not a colonist)."));
            }

            var map = Find.CurrentMap;
            return new ActorResolution(
                fallbackToAnyActiveColonist ? ResolveColonistOnMap(map, args, pawnIdKey, true) : null,
                map,
                null);
        }

        /// <summary>
        /// Resolves an active spawned colonist on the given map from <paramref name="pawnIdKey"/> argument,
        /// <see cref="ToolCallArgs.PawnId"/>, or <see cref="ToolCallArgs.NpcId"/>, with optional fallback
        /// to any standing active colonist on the map.
        /// </summary>
        public static Pawn? ResolveColonistOnMap(
            Map? map,
            ToolCallArgs args,
            string pawnIdKey = "pawn_id",
            bool fallbackToAnyActiveColonist = false)
        {
            var spawnedColonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map);
            if (spawnedColonists.Count == 0)
                return null;

            Pawn? FindActiveColonistById(int id)
            {
                if (id <= 0) return null;
                for (int i = 0; i < spawnedColonists.Count; i++)
                {
                    var p = spawnedColonists[i];
                    if (p != null && p.thingIDNumber == id && RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed)
                        return p;
                }
                return null;
            }

            if (CompositeToolCallBase.TryGetArgument<int>(args.ArgumentsJson, pawnIdKey, out int argPawnId) && argPawnId > 0)
            {
                var byArg = FindActiveColonistById(argPawnId);
                if (byArg != null) return byArg;
            }

            if (args.PawnId.HasValue && args.PawnId.Value > 0)
            {
                var byContext = FindActiveColonistById(args.PawnId.Value);
                if (byContext != null) return byContext;
            }

            if (CompositeToolCallBase.TryParsePawnIdString(args.NpcId, out int npcPawnId) && npcPawnId > 0)
            {
                var byNpc = FindActiveColonistById(npcPawnId);
                if (byNpc != null) return byNpc;
            }

            if (fallbackToAnyActiveColonist)
            {
                for (int i = 0; i < spawnedColonists.Count; i++)
                {
                    var p = spawnedColonists[i];
                    if (RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed)
                        return p;
                }
            }

            return null;
        }
    }
}
