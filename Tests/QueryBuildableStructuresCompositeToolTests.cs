using System;
using System.IO;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class QueryBuildableStructuresCompositeToolTests
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
        public void QueryBuildableStructures_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("public sealed class QueryBuildableStructuresCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_query_buildable_structures\";", source);
        }

        [Fact]
        public void QueryBuildableStructures_Declares_Category_Filter_Schema()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("\"category\"", source);
            Assert.Contains("furniture", source);
            Assert.Contains("temperature", source);
            Assert.Contains("production", source);
            Assert.Contains("recreation", source);
            Assert.Contains("defense", source);
            Assert.Contains("livestock", source);
        }

        [Fact]
        public void QueryBuildableStructures_Contains_Bottleneck_Detection_For_Beds_And_Climate()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("DetectBottlenecks", source);
            Assert.Contains("SleptOutside", source);
            Assert.Contains("SleptOnGround", source);
            Assert.Contains("PassiveCooler", source);
            Assert.Contains("Campfire", source);
        }

        [Fact]
        public void QueryBuildableStructures_Provides_Curated_Structure_Entries()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "QueryBuildableStructuresCompositeTool.cs"));
            Assert.Contains("GetCuratedStructures", source);
            Assert.Contains("Bed", source);
            Assert.Contains("PassiveCooler", source);
            Assert.Contains("FueledStove", source);
            Assert.Contains("HorseshoePin", source);
            Assert.Contains("PenMarker", source);
        }
    }
}
