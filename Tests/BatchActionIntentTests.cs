// BatchActionIntent 数据结构测试：验证默认值和属性赋值
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// BatchActionIntent 废弃 POCO 测试：验证字段默认值和赋值行为
    /// </summary>
    public class BatchActionIntentTests
    {
        [Fact]
        public void 默认IntentId为空字符串()
        {
            var intent = new BatchActionIntent();
            Assert.Equal("", intent.IntentId);
        }

        [Fact]
        public void 默认Actor为null()
        {
            var intent = new BatchActionIntent();
            Assert.Null(intent.Actor);
        }

        [Fact]
        public void 默认Target为null()
        {
            var intent = new BatchActionIntent();
            Assert.Null(intent.Target);
        }

        [Fact]
        public void 默认Param和Reason为null()
        {
            var intent = new BatchActionIntent();
            Assert.Null(intent.Param);
            Assert.Null(intent.Reason);
            Assert.Null(intent.EventId);
        }

        [Fact]
        public void 属性可正确赋值()
        {
            var actor = new Verse.Pawn();
            var target = new Verse.Pawn();
            var intent = new BatchActionIntent
            {
                IntentId = "move_to",
                Actor = actor,
                Target = target,
                Param = "100,0",
                Reason = "需要移动",
                EventId = "evt-001"
            };
            Assert.Equal("move_to", intent.IntentId);
            Assert.Same(actor, intent.Actor);
            Assert.Same(target, intent.Target);
            Assert.Equal("100,0", intent.Param);
            Assert.Equal("需要移动", intent.Reason);
            Assert.Equal("evt-001", intent.EventId);
        }
    }
}
