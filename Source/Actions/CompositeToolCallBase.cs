using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;

namespace RimMind.Actions.Actions
{
    public abstract class CompositeToolCallBase : ICompositeToolCall
    {
        public abstract string Id { get; }

        private ToolDefinition? _cachedDefinition;

        public virtual string OwnerModId => "RimMindActions";

        public virtual ToolDefinition Definition => _cachedDefinition ??= BuildDefinition();

        protected virtual ToolDefinition BuildDefinition() => new ToolDefinition { Id = Id };

        public abstract IReadOnlyList<string> RequiredToolIds { get; }

        public abstract Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct);

        protected virtual IToolHandler? FindTool(string toolId) => RimMindAPI.Tools.FindById(toolId);

        /// <summary>
        /// Checks whether all <see cref="RequiredToolIds"/> are currently registered.
        /// Composite tools can call this for early validation before execution.
        /// </summary>
        protected virtual bool AreRequiredToolsRegistered()
        {
            foreach (var toolId in RequiredToolIds)
            {
                if (FindTool(toolId) == null) return false;
            }
            return true;
        }

        protected async Task<ToolResult> ExecuteAtomicAsync(
            string toolId,
            string argumentsJson,
            ToolCallArgs parentArgs,
            CancellationToken ct)
        {
            var childCallId = $"{parentArgs.ToolCallId}:{toolId}";
            var handler = FindTool(toolId);
            if (handler == null)
            {
                return ToolResult.Fail($"Required tool not registered: {toolId}", childCallId, toolId);
            }

            var childArgs = new ToolCallArgs
            {
                ToolCallId = childCallId,
                ToolName = toolId,
                ArgumentsJson = argumentsJson,
                PawnId = parentArgs.PawnId,
                NpcId = parentArgs.NpcId,
                TraceId = parentArgs.TraceId,
                Ct = ct,
                IsPlayerApproved = parentArgs.IsPlayerApproved
            };

            var result = await handler.ExecuteAsync(childArgs, ct);
            return result.IsOk
                ? result.Value with { ToolCallId = childCallId, ToolName = toolId }
                : ToolResult.Fail($"[{result.Error.Code}] {result.Error.Message}", childCallId, toolId);
        }

        internal protected static bool TryParsePawnIdString(string? raw, out int parsedId)
        {
            parsedId = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw!.Trim();
            if (s.StartsWith("NPC-", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("pawn-", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(s.IndexOf('-') + 1).Trim();
            }
            else if (s.StartsWith("Pawn_", StringComparison.OrdinalIgnoreCase))
            {
                int idx = s.Length - 1;
                while (idx >= 0 && char.IsDigit(s[idx])) idx--;
                s = s.Substring(idx + 1);
            }
            return int.TryParse(s, out parsedId);
        }

        /// <summary>
        /// Checks whether trusted host code (e.g. an advisor approval card the player clicked) approved this call.
        /// Approval is read only from <see cref="ToolCallArgs.IsPlayerApproved"/>; LLM-supplied JSON fields such as
        /// "pre_approved" / "is_approved" are deliberately ignored so a model cannot approve its own actions.
        /// </summary>
        public static bool IsPreApproved(ToolCallArgs args) => args != null && args.IsPlayerApproved;

#if RIMMIND_ACTIONS_TESTS
        /// <summary>Test hook replacing the Verse-backed colonist target guard.</summary>
        internal static Func<int, RimMind.Domain.Agent.Policies.AutonomyDomain, ToolCallArgs, Result<Unit, RimMindError>>? TestColonistGuard;
#endif

        /// <summary>
        /// Verifies that <paramref name="pawnId"/> is an active spawned player colonist (not dead, off-map,
        /// kidnapped, prisoner, slave, guest or hostile) and that the autonomy domain allows the action.
        /// Must be called before any atomic call that targets an LLM-supplied pawn id.
        /// </summary>
        protected Result<Unit, RimMindError> GuardColonistTarget(
            int pawnId,
            RimMind.Domain.Agent.Policies.AutonomyDomain domain,
            ToolCallArgs args,
            string argumentKey = "pawn_id")
        {
#if RIMMIND_ACTIONS_TESTS
            return TestColonistGuard?.Invoke(pawnId, domain, args) ?? Result<Unit, RimMindError>.Ok(Unit.Value);
#else
            var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (pawn == null || !RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            {
                return Result<Unit, RimMindError>.Err(
                    new RimMindError(
                        RimMindErrorCode.MechanismInvalidAction,
                        $"{argumentKey} {pawnId} is not an active spawned colonist")
                    {
                        TraceId = args.TraceId
                    });
            }
            return CheckPermission(pawn, domain, args);
#endif
        }

        /// <summary>
        /// Checks autonomy permissions against the colonist and autonomy domain, safely honoring pre-approvals.
        /// </summary>
        protected Result<Unit, RimMindError> CheckPermission(Verse.Pawn? pawn, RimMind.Domain.Agent.Policies.AutonomyDomain domain, ToolCallArgs args)
        {
#if RIMMIND_ACTIONS_TESTS
            return Result<Unit, RimMindError>.Ok(Unit.Value);
#else
            return RimMind.Actions.Policies.ActionPermissionGuard.CheckPermission(pawn, Id, domain, IsPreApproved(args));
#endif
        }

        /// <summary>
        /// Tries to read a typed argument from the composite tool's JSON arguments.
        /// Returns false on missing key, null token, or conversion failure.
        /// </summary>
        internal protected static bool TryGetArgument<T>(string argumentsJson, string key, out T value)
        {
            value = default!;
            if (argumentsJson == null) return false;
            try
            {
                var token = JObject.Parse(argumentsJson)[key];
                if (token == null || token.Type == JTokenType.Null)
                {
                    return false;
                }
                if (typeof(T) == typeof(int) && token.Type == JTokenType.String)
                {
                    if (TryParsePawnIdString(token.ToString(), out int parsedInt))
                    {
                        value = (T)(object)parsedInt;
                        return true;
                    }
                    return false;
                }
                var converted = token.Value<T>();
                if (converted == null)
                {
                    return false;
                }
                value = converted;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to read a typed argument, returning a <see cref="Result{T, RimMindError}"/> with
        /// <see cref="RimMindErrorCode.MechanismInvalidAction"/> on failure. DRYs the common
        /// validate-or-return-error pattern used by composite tools.
        /// </summary>
        protected static Result<T, RimMindError> TryGetArgumentOrError<T>(
            string argumentsJson, string key, string? traceId)
        {
            if (TryGetArgument(argumentsJson, key, out T value))
            {
                return Result<T, RimMindError>.Ok(value);
            }
            return Result<T, RimMindError>.Err(
                new RimMindError(RimMindErrorCode.MechanismInvalidAction, $"Missing or invalid {key}")
                {
                    TraceId = traceId
                });
        }

        protected static Result<int, RimMindError> TryGetPawnIdOrError(
            ToolCallArgs args, string key = "pawn_id")
        {
            int contextFallbackId = (args.PawnId.HasValue && args.PawnId.Value > 0)
                ? args.PawnId.Value
                : (TryParsePawnIdString(args.NpcId, out int parsedNpcId) && parsedNpcId > 0 ? parsedNpcId : 0);

            if (TryGetArgument<int>(args.ArgumentsJson, key, out int id) && id > 0)
            {
                return Result<int, RimMindError>.Ok(id);
            }
            if (key != "pawn_id" && TryGetArgument<int>(args.ArgumentsJson, "pawn_id", out int fallbackId) && fallbackId > 0)
            {
                return Result<int, RimMindError>.Ok(fallbackId);
            }
            if (contextFallbackId > 0)
            {
                return Result<int, RimMindError>.Ok(contextFallbackId);
            }
            return Result<int, RimMindError>.Err(
                new RimMindError(RimMindErrorCode.MechanismInvalidAction, $"Missing or invalid {key}")
                {
                    TraceId = args.TraceId
                });
        }

        /// <summary>
        /// Builds a compact JSON argument payload for a child atomic tool call.
        /// </summary>
        protected static string BuildArgumentsJson(params (string Key, object? Value)[] pairs)
        {
            var obj = new JObject();
            foreach (var (key, val) in pairs)
            {
                obj[key] = val == null ? JValue.CreateNull() : JToken.FromObject(val);
            }
            return obj.ToString(Formatting.None);
        }

        /// <summary>
        /// Builds the standard per-step summary JSON: each step gets {"ok":bool,"content":string}.
        /// </summary>
        protected static string BuildStepSummary(params (string Name, ToolResult Result)[] steps)
        {
            var obj = new JObject();
            foreach (var (name, result) in steps)
            {
                obj[name] = new JObject
                {
                    ["ok"] = !result.IsError,
                    ["content"] = result.Content
                };
            }
            return obj.ToString(Formatting.None);
        }
    }
}
