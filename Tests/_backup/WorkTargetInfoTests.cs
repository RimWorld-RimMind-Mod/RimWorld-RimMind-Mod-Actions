// WorkTargetInfo 废弃数据结构测试：验证默认值和属性赋值
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// WorkTargetInfo 废弃 POCO 测试：验证字段默认值和赋值行为
    /// </summary>
    public class WorkTargetInfoTests
    {
        [Fact]
        public void 默认Distance为0()
        {
            var info = new WorkTargetInfo();
            Assert.Equal(0f, info.Distance);
        }

        [Fact]
        public void 默认Label为空字符串()
        {
            var info = new WorkTargetInfo();
            Assert.Equal("", info.Label);
        }

        [Fact]
        public void 属性可正确赋值()
        {
            var info = new WorkTargetInfo
            {
                Distance = 15.7f,
                Label = "采矿点"
            };
            Assert.Equal(15.7f, info.Distance);
            Assert.Equal("采矿点", info.Label);
        }
    }
}
