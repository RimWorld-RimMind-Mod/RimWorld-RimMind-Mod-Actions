using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class CompositeToolCallBaseTests
    {
        [Fact]
        public async Task ExecuteAtomicAsync_Passes_Child_Args_And_Normalizes_Success_Result()
        {
            using var cts = new CancellationTokenSource();
            var handler = new CapturingToolHandler(
                Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("done", "handler-id", "handler-name")));
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>
            {
                ["move_to"] = handler
            });
            var parentArgs = new ToolCallArgs
            {
                ToolCallId = "parent-1",
                ToolName = "composite",
                ArgumentsJson = "{\"ignored\":true}",
                NpcId = "npc-7",
                TraceId = "trace-9",
                Ct = CancellationToken.None
            };

            var result = await composite.ExecuteAtomicForTestAsync(
                "move_to",
                "{\"x\":1}",
                parentArgs,
                cts.Token);

            Assert.False(result.IsError);
            Assert.Equal("done", result.Content);
            Assert.Equal("parent-1:move_to", result.ToolCallId);
            Assert.Equal("move_to", result.ToolName);
            Assert.NotNull(handler.ReceivedArgs);
            Assert.Equal("parent-1:move_to", handler.ReceivedArgs.ToolCallId);
            Assert.Equal("move_to", handler.ReceivedArgs.ToolName);
            Assert.Equal("{\"x\":1}", handler.ReceivedArgs.ArgumentsJson);
            Assert.Equal("npc-7", handler.ReceivedArgs.NpcId);
            Assert.Equal("trace-9", handler.ReceivedArgs.TraceId);
            Assert.Equal(cts.Token, handler.ReceivedArgs.Ct);
            Assert.Equal(cts.Token, handler.ReceivedCancellationToken);
        }

        [Fact]
        public async Task ExecuteAtomicAsync_Missing_Handler_Returns_Error_With_Child_Id_And_Tool_Name()
        {
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>());
            var parentArgs = new ToolCallArgs { ToolCallId = "parent-2" };

            var result = await composite.ExecuteAtomicForTestAsync(
                "equip",
                "{}",
                parentArgs,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("Required tool not registered: equip", result.Content);
            Assert.Equal("parent-2:equip", result.ToolCallId);
            Assert.Equal("equip", result.ToolName);
        }

        [Fact]
        public async Task ExecuteAtomicAsync_Handler_Error_Returns_Error_With_Child_Id_And_Tool_Name()
        {
            var handler = new CapturingToolHandler(
                Result<ToolResult, RimMindError>.Err(
                    new RimMindError(RimMindErrorCode.ToolExecutionFailed, "tool failed")));
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>
            {
                ["haul"] = handler
            });
            var parentArgs = new ToolCallArgs { ToolCallId = "parent-3" };

            var result = await composite.ExecuteAtomicForTestAsync(
                "haul",
                "{}",
                parentArgs,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("tool failed", result.Content);
            Assert.Equal("parent-3:haul", result.ToolCallId);
            Assert.Equal("haul", result.ToolName);
        }

        [Fact]
        public async Task ExecuteAtomicAsync_Propagates_PawnId_From_Parent_To_Child()
        {
            var handler = new CapturingToolHandler(
                Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("done", "handler-id", "handler-name")));
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>
            {
                ["move_to"] = handler
            });
            var parentArgs = new ToolCallArgs
            {
                ToolCallId = "parent-pawn",
                ToolName = "composite",
                ArgumentsJson = "{}",
                PawnId = 42,
                NpcId = "npc-1",
                TraceId = "trace-1",
                Ct = CancellationToken.None
            };

            await composite.ExecuteAtomicForTestAsync("move_to", "{}", parentArgs, CancellationToken.None);

            Assert.NotNull(handler.ReceivedArgs);
            Assert.Equal(42, handler.ReceivedArgs!.PawnId);
        }

        [Fact]
        public void TryGetArgument_Integer_Returns_True_And_Value()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("{\"pawn_id\":123}", "pawn_id", out int value);
            Assert.True(ok);
            Assert.Equal(123, value);
        }

        [Fact]
        public void TryGetArgument_Missing_Key_Returns_False()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("{}", "pawn_id", out int value);
            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Fact]
        public void TryGetArgument_Wrong_Type_Returns_False()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("{\"pawn_id\":\"bad\"}", "pawn_id", out int value);
            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Fact]
        public void TryGetArgument_Invalid_Json_Returns_False()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("not json", "pawn_id", out int value);
            Assert.False(ok);
        }

        [Fact]
        public void TryGetArgument_Null_Json_Returns_False()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest(null!, "pawn_id", out int value);
            Assert.False(ok);
        }

        [Fact]
        public void TryGetArgument_String_Returns_True_And_Value()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("{\"action\":\"undraft\"}", "action", out string value);
            Assert.True(ok);
            Assert.Equal("undraft", value);
        }

        [Fact]
        public void TryGetArgument_Explicit_Null_Returns_False()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("{\"pawn_id\":null}", "pawn_id", out int value);
            Assert.False(ok);
        }

        [Fact]
        public void TryGetArgument_Overflow_Returns_False()
        {
            var ok = TestCompositeToolCall.TryGetArgumentForTest("{\"pawn_id\":99999999999999999999}", "pawn_id", out int value);
            Assert.False(ok);
        }

        [Fact]
        public void TryGetArgumentOrError_Valid_Value_Returns_Ok()
        {
            var result = TestCompositeToolCall.TryGetArgumentOrErrorForTest<int>(
                "{\"pawn_id\":99}", "pawn_id", "trace-1");
            Assert.True(result.IsOk);
            Assert.Equal(99, result.Value);
        }

        [Fact]
        public void TryGetArgumentOrError_Missing_Key_Returns_Err_With_MechanismInvalidAction()
        {
            var result = TestCompositeToolCall.TryGetArgumentOrErrorForTest<int>(
                "{}", "pawn_id", "trace-2");
            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
            Assert.Contains("pawn_id", result.Error.Message);
            Assert.Equal("trace-2", result.Error.TraceId);
        }

        [Fact]
        public void TryGetArgumentOrError_Invalid_Json_Returns_Err()
        {
            var result = TestCompositeToolCall.TryGetArgumentOrErrorForTest<int>(
                "bad", "pawn_id", null);
            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
        }

        [Fact]
        public void BuildArgumentsJson_Produces_Compact_Json()
        {
            var json = TestCompositeToolCall.BuildArgumentsJsonForTest(("pawn_id", 5), ("action", "undraft"));
            var parsed = JObject.Parse(json);
            Assert.Equal(5, parsed["pawn_id"]!.Value<int>());
            Assert.Equal("undraft", parsed["action"]!.Value<string>());
            Assert.False(json.Contains(" ") || json.Contains("\n"));
        }

        [Fact]
        public void BuildArgumentsJson_Empty_Pairs_Returns_Empty_Object()
        {
            var json = TestCompositeToolCall.BuildArgumentsJsonForTest();
            Assert.Equal("{}", json);
        }

        [Fact]
        public void BuildArgumentsJson_Null_Value_Produces_Json_Null()
        {
            var json = TestCompositeToolCall.BuildArgumentsJsonForTest(("key", (object?)null));
            var parsed = JObject.Parse(json);
            Assert.Equal(JTokenType.Null, parsed["key"]!.Type);
        }

        [Fact]
        public void BuildStepSummary_Reports_Per_Step_Ok_And_Content()
        {
            var summary = TestCompositeToolCall.BuildStepSummaryForTest(
                ("undraft", ToolResult.Ok("done")),
                ("rest", ToolResult.Fail("no bed")));
            var parsed = JObject.Parse(summary);
            Assert.True(parsed["undraft"]!["ok"]!.Value<bool>());
            Assert.Equal("done", parsed["undraft"]!["content"]!.Value<string>());
            Assert.False(parsed["rest"]!["ok"]!.Value<bool>());
            Assert.Equal("no bed", parsed["rest"]!["content"]!.Value<string>());
        }

        [Fact]
        public void BuildStepSummary_Empty_Steps_Returns_Empty_Object()
        {
            var summary = TestCompositeToolCall.BuildStepSummaryForTest();
            Assert.Equal("{}", summary);
        }

        [Fact]
        public void OwnerModId_Is_Virtual_And_Overridable_By_Subclass()
        {
            var tool = new CustomOwnerCompositeTool();
            Assert.Equal("ThirdPartyMod", tool.OwnerModId);
        }

        [Fact]
        public void AreRequiredToolsRegistered_All_Present_Returns_True()
        {
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>
            {
                ["test_tool"] = new CapturingToolHandler(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("ok")))
            });
            Assert.True(composite.AreRequiredToolsRegisteredForTest());
        }

        [Fact]
        public void AreRequiredToolsRegistered_Missing_Tool_Returns_False()
        {
            var composite = new TestCompositeToolCall(new Dictionary<string, IToolHandler>());
            Assert.False(composite.AreRequiredToolsRegisteredForTest());
        }

        private sealed class TestCompositeToolCall : CompositeToolCallBase
        {
            private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;

            public TestCompositeToolCall(IReadOnlyDictionary<string, IToolHandler> handlers)
            {
                _handlers = handlers;
            }

            public override string Id => "test_composite";

            public override ToolDefinition Definition => new ToolDefinition { Id = Id };

            public override IReadOnlyList<string> RequiredToolIds => new[] { "test_tool" };

            public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(
                ToolCallArgs args,
                CancellationToken ct) =>
                Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("unused")));

            public Task<ToolResult> ExecuteAtomicForTestAsync(
                string toolId,
                string argumentsJson,
                ToolCallArgs parentArgs,
                CancellationToken ct) =>
                ExecuteAtomicAsync(toolId, argumentsJson, parentArgs, ct);

            public static bool TryGetArgumentForTest<T>(string json, string key, out T value)
                => TryGetArgument(json, key, out value);

            public static Result<T, RimMindError> TryGetArgumentOrErrorForTest<T>(
                string argumentsJson, string key, string? traceId)
                => TryGetArgumentOrError<T>(argumentsJson, key, traceId);

            public static string BuildArgumentsJsonForTest(params (string Key, object? Value)[] pairs)
                => BuildArgumentsJson(pairs);

            public static string BuildStepSummaryForTest(params (string Name, ToolResult Result)[] steps)
                => BuildStepSummary(steps);

            public bool AreRequiredToolsRegisteredForTest() => AreRequiredToolsRegistered();

            protected override IToolHandler? FindTool(string toolId) =>
                _handlers.TryGetValue(toolId, out var handler) ? handler : null;
        }

        private sealed class CapturingToolHandler : IToolHandler
        {
            private readonly Result<ToolResult, RimMindError> _result;

            public CapturingToolHandler(Result<ToolResult, RimMindError> result)
            {
                _result = result;
            }

            public string Id => "capturing_tool";

            public string OwnerModId => "RimMindActions.Tests";

            public ToolDefinition Definition => new ToolDefinition { Id = Id };

            public ToolCallArgs? ReceivedArgs { get; private set; }

            public CancellationToken ReceivedCancellationToken { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                ReceivedArgs = args;
                ReceivedCancellationToken = ct;
                return Task.FromResult(_result);
            }
        }

        private sealed class CustomOwnerCompositeTool : CompositeToolCallBase
        {
            public override string Id => "test.custom_owner";
            public override ToolDefinition Definition => new ToolDefinition { Id = Id };
            public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();
            public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct) =>
                Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("ok")));
            public override string OwnerModId => "ThirdPartyMod";
        }
    }
}
