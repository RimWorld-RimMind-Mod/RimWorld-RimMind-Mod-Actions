// RimMindActionsSettings 设置逻辑测试：验证 IsAllowed 和默认值
using System.Collections.Generic;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// RimMindActionsSettings 纯逻辑测试：验证 IsAllowed 方法和字段默认值
    /// ExposeData 依赖 RimWorld 序列化框架，不在纯逻辑测试中覆盖
    /// </summary>
    public class RimMindActionsSettingsTests
    {
        [Fact]
        public void 默认DisabledIntents为空集合()
        {
            var settings = new RimMindActionsSettings();
            Assert.Empty(settings.DisabledIntents);
        }

        [Fact]
        public void 默认enableActions为true()
        {
            var settings = new RimMindActionsSettings();
            Assert.True(settings.enableActions);
        }

        [Fact]
        public void 默认delayedQueueMaxSize为50()
        {
            var settings = new RimMindActionsSettings();
            Assert.Equal(50, settings.delayedQueueMaxSize);
        }

        [Fact]
        public void 默认delayedQueueDefaultDelay为1点5()
        {
            var settings = new RimMindActionsSettings();
            Assert.Equal(1.5f, settings.delayedQueueDefaultDelay);
        }

        [Fact]
        public void IsAllowed_未禁用时返回true()
        {
            var settings = new RimMindActionsSettings();
            Assert.True(settings.IsAllowed("move_to"));
            Assert.True(settings.IsAllowed("draft"));
            Assert.True(settings.IsAllowed("trigger_incident"));
        }

        [Fact]
        public void IsAllowed_已禁用时返回false()
        {
            var settings = new RimMindActionsSettings();
            settings.DisabledIntents.Add("arrest_pawn");
            Assert.False(settings.IsAllowed("arrest_pawn"));
        }

        [Fact]
        public void IsAllowed_禁用一个不影响其他意图()
        {
            var settings = new RimMindActionsSettings();
            settings.DisabledIntents.Add("arrest_pawn");
            Assert.False(settings.IsAllowed("arrest_pawn"));
            Assert.True(settings.IsAllowed("move_to"));
            Assert.True(settings.IsAllowed("draft"));
        }

        [Fact]
        public void IsAllowed_移除禁用后恢复允许()
        {
            var settings = new RimMindActionsSettings();
            settings.DisabledIntents.Add("arrest_pawn");
            Assert.False(settings.IsAllowed("arrest_pawn"));
            settings.DisabledIntents.Remove("arrest_pawn");
            Assert.True(settings.IsAllowed("arrest_pawn"));
        }

        [Fact]
        public void DisabledIntents_可批量添加多个意图()
        {
            var settings = new RimMindActionsSettings();
            settings.DisabledIntents.Add("trigger_mental_state");
            settings.DisabledIntents.Add("trigger_incident");
            settings.DisabledIntents.Add("arrest_pawn");
            Assert.Equal(3, settings.DisabledIntents.Count);
            Assert.False(settings.IsAllowed("trigger_mental_state"));
            Assert.False(settings.IsAllowed("trigger_incident"));
            Assert.False(settings.IsAllowed("arrest_pawn"));
            Assert.True(settings.IsAllowed("move_to"));
        }

        [Fact]
        public void IsAllowed_空字符串意图在未禁用时返回true()
        {
            var settings = new RimMindActionsSettings();
            Assert.True(settings.IsAllowed(""));
        }

        [Fact]
        public void IsAllowed_大小写敏感()
        {
            var settings = new RimMindActionsSettings();
            settings.DisabledIntents.Add("Move_To");
            Assert.False(settings.IsAllowed("Move_To"));
            Assert.True(settings.IsAllowed("move_to"));
        }
    }
}
