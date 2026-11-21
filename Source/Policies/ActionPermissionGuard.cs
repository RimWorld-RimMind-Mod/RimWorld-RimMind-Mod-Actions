using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;
using CompositeToolCallBase = RimMind.Actions.Actions.CompositeToolCallBase;

namespace RimMind.Actions.Policies
{
    /// <summary>
    /// Guard that enforces colonist tri-state autonomy permissions and resolves actor/map targets
    /// via Core <see cref="ActorMapResolver"/> and <see cref="RimMindAPI.Text"/>.
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
            => RimMindAPI.Text.TrOr(key, fallbackEn, args);

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
        /// Resolves the acting colonist across all maps via Core <see cref="ActorMapResolver"/> and returns
        /// <em>that colonist's</em> map, so actions never land on whatever map the player happens to be viewing.
        /// Lookup order: <paramref name="pawnIdKey"/> argument, <see cref="ToolCallArgs.PawnId"/>,
        /// <see cref="ToolCallArgs.NpcId"/>.
        /// </summary>
        public static ActorResolution ResolveActorAndMap(
            ToolCallArgs args,
            string pawnIdKey = "pawn_id",
            bool fallbackToAnyActiveColonist = false,
            JObject? parsedArgs = null)
        {
            var root = parsedArgs ?? CompositeToolCallBase.ParseArgumentsObject(args.ArgumentsJson);
            bool anyIdSupplied = false;

            Pawn? TryResolve(int id, out Map? resolvedMap)
            {
                resolvedMap = null;
                if (id <= 0) return null;
                anyIdSupplied = true;
                if (ActorMapResolver.ResolveColonistAndMap(id, allowDowned: false, out var pawn, out var map, out _, args.TraceId))
                {
                    resolvedMap = map;
                    return pawn;
                }
                return null;
            }

            Pawn? actor = null;
            Map? actorMap = null;

            if (CompositeToolCallBase.TryReadArgument<int>(root, pawnIdKey, out int argPawnId))
                actor = TryResolve(argPawnId, out actorMap);
            if (actor == null && args.PawnId.HasValue)
                actor = TryResolve(args.PawnId.Value, out actorMap);
            if (actor == null && CompositeToolCallBase.TryParsePawnId(args.NpcId, out int npcPawnId))
                actor = TryResolve(npcPawnId, out actorMap);

            if (actor != null)
                return new ActorResolution(actor, actorMap ?? actor.Map, null);

            if (anyIdSupplied)
            {
                return new ActorResolution(null, null,
                    TranslateOrFallback(
                        "RimMind.Actions.Permission.InvalidColonist",
                        "Requested colonist is not an active, standing colonist on any map (dead, downed, off-map, prisoner or not a colonist)."));
            }

            var map = ActorMapResolver.ResolveMap(null);
            return new ActorResolution(
                fallbackToAnyActiveColonist ? ResolveColonistOnMap(map, args, pawnIdKey, true, root) : null,
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
            bool fallbackToAnyActiveColonist = false,
            JObject? parsedArgs = null)
        {
            var spawnedColonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map);
            if (spawnedColonists.Count == 0)
                return null;

            var root = parsedArgs ?? CompositeToolCallBase.ParseArgumentsObject(args.ArgumentsJson);

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

            if (CompositeToolCallBase.TryReadArgument<int>(root, pawnIdKey, out int argPawnId) && argPawnId > 0)
            {
                var byArg = FindActiveColonistById(argPawnId);
                if (byArg != null) return byArg;
            }

            if (args.PawnId.HasValue && args.PawnId.Value > 0)
            {
                var byContext = FindActiveColonistById(args.PawnId.Value);
                if (byContext != null) return byContext;
            }

            if (CompositeToolCallBase.TryParsePawnId(args.NpcId, out int npcPawnId) && npcPawnId > 0)
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
