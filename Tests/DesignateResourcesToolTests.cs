using System;
using System.IO;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class DesignateResourcesToolTests
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
        public void DesignateResources_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "DesignateResourcesCompositeTool.cs"));
            Assert.Contains("public sealed class DesignateResourcesCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_designate_resources\";", source);
        }

        [Fact]
        public void DesignateResources_Declares_Parameters_Schema()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "DesignateResourcesCompositeTool.cs"));
            Assert.Contains("\"designation_type\"", source);
            Assert.Contains("\"radius\"", source);
            Assert.Contains("\"target_name\"", source);
            Assert.Contains("\"max_count\"", source);
            Assert.Contains("\"pawn_id\"", source);
        }

        [Fact]
        public void DesignateResources_Integrates_ResourceMarking_Autonomy_Guard()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "DesignateResourcesCompositeTool.cs"));
            Assert.Contains("ActionPermissionGuard.CheckPermission", source);
            Assert.Contains("AutonomyDomain.ResourceMarking", source);
        }

        [Fact]
        public void DesignateResources_Supports_Harvest_ChopWood_And_Mine()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "DesignateResourcesCompositeTool.cs"));
            Assert.Contains("DesignationDefOf.HarvestPlant", source);
            Assert.Contains("DesignationDefOf.CutPlant", source);
            Assert.Contains("DesignationDefOf.Mine", source);
            Assert.Contains("AddDesignation", source);
        }
    }
}
