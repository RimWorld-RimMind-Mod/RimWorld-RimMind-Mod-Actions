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
    }
}

namespace RimMind.Presentation
{
    public static class RimMindAPI
    {
        public static RimMind.Application.Common.Interfaces.Tools.IToolRegistry Tools { get; set; } = null!;
    }
}
