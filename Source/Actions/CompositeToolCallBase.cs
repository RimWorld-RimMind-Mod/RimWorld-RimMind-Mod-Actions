using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Actions-module base class for composite ToolCalls.
    /// Inherits Core's <see cref="RimMind.Presentation.Api.CompositeToolCallBase"/>
    /// (<c>ExecuteTemplateAsync</c>, <c>ExecuteColonistToolAsync</c>, <c>ExecuteAtomicAsync</c>,
    /// single-pass <c>TryParseArgumentsObject</c>, <c>Classify</c>, <c>Manifest</c>, and <c>ToolScopes</c>),
    /// layering Actions-specific ownership and localized permission guards on top.
    /// </summary>
    public abstract class CompositeToolCallBase : RimMind.Presentation.Api.CompositeToolCallBase
    {
        public override string OwnerModId => RimMindOwnerConsts.ActionsModId;

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
            var threadGuard = EnsureMainThread(args);
            if (threadGuard.IsErr)
                return threadGuard;
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
            return RimMind.Actions.Policies.ActionPermissionGuard.CheckPermission(pawn, Id, domain, args);
#endif
        }

        /// <summary>
        /// Runs an Actions direct-Verse composite tool through Core's guarded execution template:
        /// cancellation check, Unity main-thread guard, single-pass JSON argument parsing, and
        /// structured exception handling.
        /// </summary>
        protected Task<Result<ToolResult, RimMindError>> ExecuteDirectToolAsync(
            ToolCallArgs? args,
            Func<JObject?, ToolCallArgs, CancellationToken, Task<Result<ToolResult, RimMindError>>> body,
            CancellationToken ct)
            => ExecuteTemplateAsync(args, body, ct, requireMainThread: true);
    }
}
