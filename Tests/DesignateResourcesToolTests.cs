using System;
using System.IO;
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
    public class DesignateResourcesToolTests
    {
        private static readonly string RepoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        [Fact]
        public void DesignateResources_Declares_Metadata_And_Accurate_Parameters_Schema()
        {
            var tool = new DesignateResourcesCompositeTool();
            Assert.Equal("actions_designate_resources", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            var props = schema["properties"] as JObject;
            Assert.NotNull(props);
            Assert.NotNull(props!["designation_type"]);
            Assert.NotNull(props["radius"]);
            Assert.Contains("60", props["radius"]!["description"]!.ToString());
            Assert.Contains("120", props["radius"]!["description"]!.ToString());
            Assert.NotNull(props["target_name"]);
            Assert.NotNull(props["max_count"]);
            Assert.NotNull(props["pawn_id"]);

            var decision = tool.Classify(new ToolCallArgs { ToolName = tool.Id, ArgumentsJson = "{\"designation_type\":\"ChopWood\"}" });
            Assert.Equal(AutonomyDomain.ResourceMarking, decision.Domain);
        }

        [Fact]
        public async Task DesignateResources_Validates_Input_MainThread_And_Guard()
        {
            var tool = new DesignateResourcesCompositeTool();
            try
            {
                var missing = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "res-missing",
                        ToolName = tool.Id,
                        ArgumentsJson = "{}",
                        TraceId = "trace-res-missing"
                    },
                    CancellationToken.None);

                Assert.True(missing.IsErr);
                Assert.Equal(RimMindErrorCode.MechanismInvalidAction, missing.Error.Code);
                Assert.Equal("trace-res-missing", missing.Error.TraceId);

                Verse.UnityData.IsInMainThread = false;
                var offThread = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "res-thread",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"designation_type\":\"ChopWood\"}"
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

                var denied = await tool.ExecuteAsync(
                    new ToolCallArgs
                    {
                        ToolCallId = "res-guard",
                        ToolName = tool.Id,
                        ArgumentsJson = "{\"designation_type\":\"ChopWood\",\"pawn_id\":9}"
                    },
                    CancellationToken.None);
                Assert.True(denied.IsErr);
                Assert.Equal(AutonomyDomain.ResourceMarking, capturedDomain);
            }
            finally
            {
                Verse.UnityData.IsInMainThread = true;
                CompositeToolCallBase.TestColonistGuard = null;
            }
        }

        [Fact]
        public void No_CompositeTool_Mutates_Player_WorkPriorities_Via_SetPriority()
        {
            var actionsDir = Path.Combine(RepoRoot, "RimMind-Actions", "Source", "Actions");
            foreach (var file in Directory.GetFiles(actionsDir, "*.cs"))
            {
                var source = File.ReadAllText(file);
                Assert.DoesNotContain(".SetPriority(", source);
            }
        }
    }
}
