// ActionResult 废弃兼容层测试：验证默认值和 ToString 格式化
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// ActionResult 废弃 POCO 测试：验证默认值、ToString 格式化行为
    /// </summary>
    public class ActionResultDeprecatedTests
    {
        [Fact]
        public void 默认ActionName为空字符串()
        {
            var result = new ActionResult();
            Assert.Equal("", result.ActionName);
        }

        [Fact]
        public void 默认Success为false()
        {
            var result = new ActionResult();
            Assert.False(result.Success);
        }

        [Fact]
        public void 默认Reason为空字符串()
        {
            var result = new ActionResult();
            Assert.Equal("", result.Reason);
        }

        [Fact]
        public void ToString_成功时显示OK格式()
        {
            var result = new ActionResult
            {
                ActionName = "move_to",
                Success = true
            };
            Assert.Equal("OK: move_to", result.ToString());
        }

        [Fact]
        public void ToString_失败时显示FAIL格式含原因()
        {
            var result = new ActionResult
            {
                ActionName = "arrest_pawn",
                Success = false,
                Reason = "目标不存在"
            };
            Assert.Equal("FAIL: arrest_pawn (目标不存在)", result.ToString());
        }

        [Fact]
        public void ToString_失败无原因时显示空括号()
        {
            var result = new ActionResult
            {
                ActionName = "draft",
                Success = false,
                Reason = ""
            };
            Assert.Equal("FAIL: draft ()", result.ToString());
        }

        [Fact]
        public void 属性可正确赋值()
        {
            var result = new ActionResult
            {
                ActionName = "eat_food",
                Success = true,
                Reason = "已找到食物"
            };
            Assert.Equal("eat_food", result.ActionName);
            Assert.True(result.Success);
            Assert.Equal("已找到食物", result.Reason);
        }
    }
}
