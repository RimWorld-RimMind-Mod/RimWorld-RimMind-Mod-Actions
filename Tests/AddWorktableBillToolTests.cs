using System;
using System.IO;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class AddWorktableBillToolTests
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
        public void AddWorktableBill_Declares_Correct_Id_And_Extends_CompositeToolCallBase()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "AddWorktableBillCompositeTool.cs"));
            Assert.Contains("public sealed class AddWorktableBillCompositeTool : CompositeToolCallBase", source);
            Assert.Contains("public override string Id => \"actions_add_worktable_bill\";", source);
        }

        [Fact]
        public void AddWorktableBill_Declares_Parameters_Schema()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "AddWorktableBillCompositeTool.cs"));
            Assert.Contains("\"recipe_def\"", source);
            Assert.Contains("\"worktable_def\"", source);
            Assert.Contains("\"target_count\"", source);
            Assert.Contains("\"repeat_mode\"", source);
            Assert.Contains("\"pawn_id\"", source);
        }

        [Fact]
        public void AddWorktableBill_Integrates_Autonomy_Permission_Check()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "AddWorktableBillCompositeTool.cs"));
            Assert.Contains("ActionPermissionGuard.CheckPermission", source);
            Assert.Contains("AutonomyDomain.WorktableBills", source);
        }

        [Fact]
        public void AddWorktableBill_Handles_Duplicate_Prevention_And_TargetCount()
        {
            var source = ReadActionsSource(Path.Combine("Actions", "AddWorktableBillCompositeTool.cs"));
            Assert.Contains("billStack", source);
            Assert.Contains("BillRepeatModeDefOf.TargetCount", source);
            Assert.Contains("AddBill", source);
        }
    }
}
