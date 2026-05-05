using RimMind.Actions;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class ActionResultTests
    {
        [Fact]
        public void Succeeded_SetsProperties()
        {
            var result = ActionResult.Succeeded("move_to", "targetA");
            Assert.True(result.Success);
            Assert.Equal("move_to", result.ActionName);
            Assert.Equal("", result.Reason);
            Assert.Equal("targetA", result.TargetLabel);
        }

        [Fact]
        public void Succeeded_WithoutTarget()
        {
            var result = ActionResult.Succeeded("cancel_job");
            Assert.True(result.Success);
            Assert.Equal("", result.TargetLabel);
        }

        [Fact]
        public void Failed_SetsProperties()
        {
            var result = ActionResult.Failed("arrest_pawn", "not reachable", "pawnB");
            Assert.False(result.Success);
            Assert.Equal("arrest_pawn", result.ActionName);
            Assert.Equal("not reachable", result.Reason);
            Assert.Equal("pawnB", result.TargetLabel);
        }

        [Fact]
        public void Failed_WithoutTarget()
        {
            var result = ActionResult.Failed("move_to", "blocked");
            Assert.False(result.Success);
            Assert.Equal("", result.TargetLabel);
        }

        [Fact]
        public void ImplicitBool_TrueWhenSucceeded()
        {
            var result = ActionResult.Succeeded("test");
            Assert.True(result);
        }

        [Fact]
        public void ImplicitBool_FalseWhenFailed()
        {
            var result = ActionResult.Failed("test", "reason");
            Assert.False(result);
        }

        [Fact]
        public void ToString_Succeeded_WithTarget()
        {
            var result = ActionResult.Succeeded("move_to", "spotA");
            Assert.Equal("[OK] move_to → spotA", result.ToString());
        }

        [Fact]
        public void ToString_Succeeded_WithoutTarget()
        {
            var result = ActionResult.Succeeded("cancel_job");
            Assert.Equal("[OK] cancel_job", result.ToString());
        }

        [Fact]
        public void ToString_Failed_WithTarget()
        {
            var result = ActionResult.Failed("arrest_pawn", "too far", "pawnB");
            Assert.Equal("[FAIL] arrest_pawn → pawnB: too far", result.ToString());
        }

        [Fact]
        public void ToString_Failed_WithoutTarget()
        {
            var result = ActionResult.Failed("move_to", "blocked");
            Assert.Equal("[FAIL] move_to: blocked", result.ToString());
        }

        [Fact]
        public void NullActionName_TreatedAsEmpty()
        {
            var result = ActionResult.Succeeded(null!);
            Assert.Equal("", result.ActionName);
        }

        [Fact]
        public void NullReason_TreatedAsEmpty()
        {
            var result = ActionResult.Failed("test", null!);
            Assert.Equal("", result.Reason);
        }
    }
}
