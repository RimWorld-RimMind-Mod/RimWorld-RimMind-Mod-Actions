using System;
using System.IO;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class ManageZoneAndEquipmentToolTests
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
        public void ManageZone_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageZoneCompositeTool.cs"));
            Assert.Contains("public sealed class ManageZoneCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_manage_zone\";", source);
        }

        [Fact]
        public void ManageZone_Declares_Parameters_Schema()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageZoneCompositeTool.cs"));
            Assert.Contains("\"zone_type\"", source);
            Assert.Contains("\"crop_def\"", source);
            Assert.Contains("\"width\"", source);
            Assert.Contains("\"height\"", source);
            Assert.Contains("\"pawn_id\"", source);
        }

        [Fact]
        public void ManageZone_Integrates_ResourceMarking_Autonomy_Guard()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageZoneCompositeTool.cs"));
            Assert.Contains("ActionPermissionGuard.CheckPermission", source);
            Assert.Contains("AutonomyDomain.ResourceMarking", source);
        }

        [Fact]
        public void ManageZone_Supports_Zone_Growing_And_Stockpile()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageZoneCompositeTool.cs"));
            Assert.Contains("Zone_Growing", source);
            Assert.Contains("Zone_Stockpile", source);
            Assert.Contains("RegisterZone", source);
            Assert.Contains("AddCell", source);
        }

        [Fact]
        public void ManageEquipment_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageEquipmentCompositeTool.cs"));
            Assert.Contains("public sealed class ManageEquipmentCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_manage_equipment\";", source);
        }

        [Fact]
        public void ManageEquipment_Integrates_EmergencySurvival_Autonomy_Guard()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageEquipmentCompositeTool.cs"));
            Assert.Contains("ActionPermissionGuard.CheckPermission", source);
            Assert.Contains("AutonomyDomain.EmergencySurvival", source);
        }

        [Fact]
        public void ManageEquipment_Issues_Wear_Job_On_Pawn()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "ManageEquipmentCompositeTool.cs"));
            Assert.Contains("JobDefOf.Wear", source);
            Assert.Contains("TryTakeOrderedJob", source);
        }
    }
}
