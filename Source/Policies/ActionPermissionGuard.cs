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
        {
            try
            {
                if (!string.IsNullOrEmpty(key) && key.CanTranslate())
                {
                    string template = key.TranslateSimple();
                    return args != null && args.Length > 0 ? string.Format(template, args) : template;
                }
            }
            catch
            {
            }
            return args != null && args.Length > 0 ? string.Format(fallbackEn, args) : fallbackEn;
        }

        public static Result<Unit, RimMindError> CheckPermission(Pawn? pawn, string toolId, AutonomyDomain domain)
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
            if (map?.mapPawns?.FreeColonistsSpawned == null)
                return null;

            Pawn? FindActiveColonistById(int id)
            {
                if (id <= 0) return null;
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (p != null && p.thingIDNumber == id && RimMindPawnLookup.IsActiveSpawnedColonist(p))
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
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (RimMindPawnLookup.IsActiveSpawnedColonist(p) && !p.Downed)
                        return p;
                }
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (RimMindPawnLookup.IsActiveSpawnedColonist(p))
                        return p;
                }
            }

            return null;
        }
    }
}
