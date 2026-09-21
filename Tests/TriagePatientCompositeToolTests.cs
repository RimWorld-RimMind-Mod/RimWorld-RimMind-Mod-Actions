using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class TriagePatientCompositeToolTests : IDisposable
    {
        private readonly IToolRegistry? _originalTools;

        public TriagePatientCompositeToolTests()
        {
            _originalTools = RimMindAPI.Tools;
        }

        public void Dispose()
        {
            RimMindAPI.Tools = _originalTools!;
        }

        [Fact]
        public async Task ExecuteAsync_InvalidPatientId_Returns_Err_Result()
        {
            var tool = new TriagePatientCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-triage-1",
                    ToolName = "actions.triage_patient",
                    ArgumentsJson = "{\"doctor_id\":10}"
                },
                CancellationToken.None);

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismInvalidAction, result.Error.Code);
            Assert.Equal("Missing or invalid patient_id", result.Error.Message);
        }

        [Fact]
        public async Task ExecuteAsync_WithDoctorAndPatient_CoordinatesBoth()
        {
            var registry = new FakeToolRegistry();
            var job = new RecordingHandler("pawn.job.set", ToolResult.Ok("ok"));
            var thought = new RecordingHandler("pawn.thought.add", ToolResult.Ok("stabilized"));
            registry.Register(job);
            registry.Register(thought);
            RimMindAPI.Tools = registry;

            var tool = new TriagePatientCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-triage-2",
                    ToolName = "actions.triage_patient",
                    ArgumentsJson = "{\"patient_id\":55,\"doctor_id\":12,\"urgency\":\"Critical\"}"
                },
                CancellationToken.None);

            Assert.True(result.IsOk);
            var parsed = JObject.Parse(result.Value.Content);
            Assert.True((bool)parsed["patientBedRest"]!["ok"]!);
            Assert.True((bool)parsed["doctorTriageReady"]!["ok"]!);
            Assert.True((bool)parsed["stabilizeShock"]!["ok"]!);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutDoctor_OperatesGracefully()
        {
            var registry = new FakeToolRegistry();
            var job = new RecordingHandler("pawn.job.set", ToolResult.Ok("ok"));
            var thought = new RecordingHandler("pawn.thought.add", ToolResult.Ok("stabilized"));
            registry.Register(job);
            registry.Register(thought);
            RimMindAPI.Tools = registry;

            var tool = new TriagePatientCompositeTool();

            var result = await tool.ExecuteAsync(
                new ToolCallArgs
                {
                    ToolCallId = "parent-triage-3",
                    ToolName = "actions.triage_patient",
                    ArgumentsJson = "{\"patient_id\":55}"
                },
                CancellationToken.None);

            Assert.True(result.IsOk);
            var parsed = JObject.Parse(result.Value.Content);
            Assert.True((bool)parsed["patientBedRest"]!["ok"]!);
            Assert.True((bool)parsed["stabilizeShock"]!["ok"]!);
        }

        [Fact]
        public void Definition_And_RequiredToolIds_AreAccurate()
        {
            var tool = new TriagePatientCompositeTool();
            Assert.Equal("actions.triage_patient", tool.Id);
            Assert.Equal("composite", tool.Definition.Category);
            Assert.Contains("pawn.job.set", tool.RequiredToolIds);
            Assert.Contains("pawn.thought.add", tool.RequiredToolIds);
        }
    }
}
