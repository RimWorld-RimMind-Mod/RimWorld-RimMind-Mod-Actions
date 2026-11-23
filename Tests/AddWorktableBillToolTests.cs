using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class AddWorktableBillToolTests
    {
        [Fact]
        public void AddWorktableBill_Declares_Metadata_And_Parameters_Schema()
        {
            var tool = new AddWorktableBillCompositeTool();
            Assert.Equal("actions_add_worktable_bill", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            var props = schema["properties"] as JObject;
            Assert.NotNull(props);
            Assert.NotNull(props!["recipe_def"]);
            Assert.NotNull(props["worktable_def"]);
            Assert.NotNull(props["target_count"]);
            Assert.NotNull(props["repeat_mode"]);
            Assert.NotNull(props["pawn_id"]);
        }

        [Fact]
        public async Task AddWorktableBill_Validates_Input_MainThread_And_DomainGuard()
        {
            var tool = new AddWorktableBillCompositeTool();
            try
            {
                var missing = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bill-missing",
                        ToolName = tool.Id,
                        ArgumentsJson = "{}",
                        TraceId = "trace-bill-missing"
                    },
                    CancellationToken.None);

                Assert.True(missing.IsErr);
                Assert.Equal(RimMindErrorCode.MechanismInvalidAction, missing.Error.Code);
                Assert.Equal("trace-bill-missing", missing.Error.TraceId);

                Verse.UnityData.IsInMainThread = false;
                var offThread = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bill-thread",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"recipe_def\":\"CookMealSimple\"}"
                    },
                    CancellationToken.None);
                Assert.True(offThread.IsErr);
                Assert.Equal(RimMindErrorCode.ToolExecutionFailed, offThread.Error.Code);

                Verse.UnityData.IsInMainThread = true;
                AutonomyDomain? capturedDomain = null;
                CompositeToolCallBase.TestColonistGuard = (_, domain, _) =>
                {
                    capturedDomain = domain;
                    return Result<Unit, RimMindError>.Err(RimMindErrors.ToolPolicyDenied(tool.Id, "denied"));
                };

                var nonSurvival = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bill-craft",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"recipe_def\":\"Make_SculptureSmall\",\"pawn_id\":5}"
                    },
                    CancellationToken.None);
                Assert.True(nonSurvival.IsErr);
                Assert.Equal(AutonomyDomain.WorktableBills, capturedDomain);

                var survival = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "bill-cook",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"recipe_def\":\"CookMealSimple\",\"pawn_id\":5}"
                    },
                    CancellationToken.None);
                Assert.True(survival.IsErr);
                Assert.Equal(AutonomyDomain.EmergencySurvival, capturedDomain);
            }
            finally
            {
                Verse.UnityData.IsInMainThread = true;
                CompositeToolCallBase.TestColonistGuard = null;
            }
        }
    }
}
