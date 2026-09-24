using System;
using System.IO;
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
        public void Composite_Base_Implements_ICompositeToolCall()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "CompositeToolCallBase.cs"));
            Assert.Contains("ICompositeToolCall", source);
            Assert.Contains("ExecuteAtomicAsync", source);
        }

        [Fact]
        public void StabilizeRest_Declares_Atomic_Tool_Dependencies()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "StabilizeRestCompositeTool.cs"));
            Assert.Contains("actions.stabilize_rest", source);
            Assert.Contains("pawn.draft.toggle", source);
            Assert.Contains("pawn.job.set", source);
            Assert.Contains("force_rest", source);
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
    }
}
