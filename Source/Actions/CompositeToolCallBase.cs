using System;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Actions-module base class for composite ToolCalls.
    /// Inherits Core's <see cref="RimMind.Presentation.Api.CompositeToolCallBase"/>
    /// (<c>ExecuteAtomicAsync</c>, single-pass <c>TryParseArgumentsObject</c>, <c>Classify</c>,
    /// <c>Manifest</c>, and <c>ToolScopes</c>), layering Actions-specific ownership and localized
    /// permission guards on top.
    /// </summary>
    public abstract class CompositeToolCallBase : RimMind.Presentation.Api.CompositeToolCallBase, ICompositeToolCall
    {
        public override string OwnerModId => "RimMindActions";

#if RIMMIND_ACTIONS_TESTS
        /// <summary>Test hook replacing the Verse-backed colonist target guard.</summary>
        internal static Func<int, AutonomyDomain, ToolCallArgs, Result<Unit, RimMindError>>? TestColonistGuard;
#endif

        protected override Result<Unit, RimMindError> GuardColonistTarget(
            int pawnId,
            AutonomyDomain domain,
            ToolCallArgs args,
            string argumentKey = "pawn_id")
        {
#if RIMMIND_ACTIONS_TESTS
            return TestColonistGuard?.Invoke(pawnId, domain, args) ?? Result<Unit, RimMindError>.Ok(Unit.Value);
#else
            return base.GuardColonistTarget(pawnId, domain, args, argumentKey);
#endif
        }

        protected override Result<Unit, RimMindError> CheckPermission(
            Verse.Pawn? pawn,
            AutonomyDomain domain,
            ToolCallArgs? args)
        {
#if RIMMIND_ACTIONS_TESTS
            return Result<Unit, RimMindError>.Ok(Unit.Value);
#else
            return RimMind.Actions.Policies.ActionPermissionGuard.CheckPermission(pawn, Id, domain, IsPreApproved(args));
#endif
        }

        internal static JObject? ParseArgumentsObject(string? argumentsJson)
            => TryParseArgumentsObject(argumentsJson);

        internal static bool TryReadArgument<T>(JObject? root, string key, out T value)
            => TryGetArgument(root, key, out value);

        internal static bool TryReadArgument<T>(string? argumentsJson, string key, out T value)
            => TryGetArgument(argumentsJson, key, out value);

        internal static bool TryParsePawnId(string? raw, out int parsedId)
            => TryParsePawnIdString(raw, out parsedId);
    }
}
