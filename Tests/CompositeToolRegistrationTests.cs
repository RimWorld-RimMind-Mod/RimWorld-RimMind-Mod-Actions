using System;
using System.IO;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class CompositeToolRegistrationTests
    {
        private static readonly string RepoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string ReadActionsSource(string relativePath)
        {
            var path = Path.Combine(RepoRoot, "RimMind-Actions", "Source", relativePath);
            Assert.True(File.Exists(path), $"Missing source file: {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Composite_Base_Implements_Core_Contracts()
        {
            Assert.True(typeof(ICompositeToolCall).IsAssignableFrom(typeof(CompositeToolCallBase)));
            Assert.True(typeof(IToolPolicyClassifier).IsAssignableFrom(typeof(CompositeToolCallBase)));
            Assert.True(typeof(RimMind.Presentation.Api.CompositeToolCallBase).IsAssignableFrom(typeof(CompositeToolCallBase)));
        }

        [Fact]
        public void StabilizeRest_Declares_Atomic_Tool_Dependencies()
        {
            var tool = new StabilizeRestCompositeTool();
            Assert.Equal("actions.stabilize_rest", tool.Id);
            Assert.Contains("pawn.draft.toggle", tool.RequiredToolIds);
            Assert.Contains("pawn.job.set", tool.RequiredToolIds);
        }

        [Fact]
        public void ActionsMod_Registers_Composite_Tools_Via_Registrar()
        {
            var source = ReadActionsSource("RimMindActionsMod.cs");
            Assert.Contains("RegisterCompositeTools", source);
            Assert.Contains("CompositeToolRegistrar.RegisterAll", source);
        }
    }
}

namespace RimMind.Presentation.Api
{
    public static class RimMindAPI
    {
        public static RimMind.Application.Common.Interfaces.Tools.IToolRegistry Tools { get; set; } = null!;

        public static class Autonomy
        {
            public static RimMind.Domain.Agent.Policies.AutonomyLevel GetLevel(
                Verse.Pawn? pawn,
                RimMind.Domain.Agent.Policies.AutonomyDomain domain)
                => RimMind.Domain.Agent.Policies.AutonomyLevel.FullAutonomous;
        }
    }

    public static class RimMindPawnLookup
    {
        public static Verse.Pawn? FindPawnByNumber(int pawnId) => null;
        public static bool IsActiveSpawnedColonist(Verse.Pawn? pawn) => pawn != null;
    }
}
