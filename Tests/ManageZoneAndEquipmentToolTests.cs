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
    public class ManageZoneAndEquipmentToolTests
    {
        [Fact]
        public void ManageZone_Declares_Metadata_And_Parameters_Schema()
        {
            var tool = new ManageZoneCompositeTool();
            Assert.Equal("actions_manage_zone", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            var props = schema["properties"] as JObject;
            Assert.NotNull(props);
            Assert.NotNull(props!["zone_type"]);
            Assert.NotNull(props["crop_def"]);
            Assert.NotNull(props["width"]);
            Assert.NotNull(props["height"]);
            Assert.NotNull(props["pawn_id"]);

            var decision = tool.Classify(new ToolCallArgs { ToolName = tool.Id, ArgumentsJson = "{\"zone_type\":\"Growing\"}" });
            Assert.Equal(AutonomyDomain.ResourceMarking, decision.Domain);
        }

        [Fact]
        public async Task ManageZone_Rejects_Missing_Or_Unknown_ZoneType_And_Accepts_Growing_Or_Stockpile()
        {
            var tool = new ManageZoneCompositeTool();

            var missing = await tool.ExecuteAsync(
                new ToolCallArgs { ToolCallId = "z-1", ToolName = tool.Id, ArgumentsJson = "{}", TraceId = "trace-z1" },
                CancellationToken.None);
            Assert.True(missing.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, missing.Error.Code);
            Assert.Equal("trace-z1", missing.Error.TraceId);

            var unknown = await tool.ExecuteAsync(
                new ToolCallArgs { ToolCallId = "z-2", ToolName = tool.Id, ArgumentsJson = "{\"zone_type\":\"PartyArea\"}", TraceId = "trace-z2" },
                CancellationToken.None);
            Assert.True(unknown.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, unknown.Error.Code);
            Assert.Contains("PartyArea", unknown.Error.Message);
            Assert.Equal("trace-z2", unknown.Error.TraceId);

            var growing = await tool.ExecuteAsync(
                new ToolCallArgs { ToolCallId = "z-3", ToolName = tool.Id, ArgumentsJson = "{\"zone_type\":\"Growing\"}" },
                CancellationToken.None);
            Assert.True(growing.IsOk);

            var stockpile = await tool.ExecuteAsync(
                new ToolCallArgs { ToolCallId = "z-4", ToolName = tool.Id, ArgumentsJson = "{\"zone_type\":\"Stockpile\"}" },
                CancellationToken.None);
            Assert.True(stockpile.IsOk);
        }

        [Fact]
        public void ManageEquipment_Declares_Metadata_And_Parameters_Schema()
        {
            var tool = new ManageEquipmentCompositeTool();
            Assert.Equal("actions_manage_equipment", tool.Id);
            Assert.IsAssignableFrom<CompositeToolCallBase>(tool);

            var schema = JObject.Parse(tool.Definition.ParametersSchema);
            var props = schema["properties"] as JObject;
            Assert.NotNull(props);
            Assert.NotNull(props!["pawn_id"]);
            Assert.NotNull(props["purpose"]);
            Assert.NotNull(props["target_apparel_def"]);

            var decision = tool.Classify(new ToolCallArgs { ToolName = tool.Id, ArgumentsJson = "{\"pawn_id\":7,\"purpose\":\"Armor\"}" });
            Assert.Equal(AutonomyDomain.EmergencySurvival, decision.Domain);
        }

        [Fact]
        public async Task ManageEquipment_Enforces_PawnId_MainThread_And_EmergencySurvival_Guard()
        {
            var tool = new ManageEquipmentCompositeTool();
            try
            {
                var missing = await tool.ExecuteAsync(
                    new ToolCallArgs { ToolCallId = "eq-1", ToolName = tool.Id, ArgumentsJson = "{}" },
                    CancellationToken.None);
                Assert.True(missing.IsErr);
                Assert.Equal(RimMindErrorCode.MechanismInvalidAction, missing.Error.Code);

                Verse.UnityData.IsInMainThread = false;
                var offThread = await tool.ExecuteAsync(
                    new ToolCallArgs { ToolCallId = "eq-2", ToolName = tool.Id, ArgumentsJson = "{\"pawn_id\":7}" },
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
                    new ToolCallArgs { ToolCallId = "eq-3", ToolName = tool.Id, ArgumentsJson = "{\"pawn_id\":7,\"purpose\":\"Armor\"}" },
                    CancellationToken.None);
                Assert.True(denied.IsErr);
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
