// TraceContext 追踪上下文测试：验证 AsyncLocal 作用域和嵌套行为
using System;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// TraceContext 测试：验证 BeginScope/Current 的 AsyncLocal 行为和嵌套恢复
    /// </summary>
    public class TraceContextTests
    {
        [Fact]
        public void Current_默认为null()
        {
            Assert.Null(TraceContext.Current);
        }

        [Fact]
        public void BeginScope_设置Current()
        {
            using (TraceContext.BeginScope("trace-001"))
            {
                Assert.Equal("trace-001", TraceContext.Current);
            }
        }

        [Fact]
        public void BeginScope_离开作用域后恢复null()
        {
            using (TraceContext.BeginScope("trace-002"))
            {
                Assert.Equal("trace-002", TraceContext.Current);
            }
            Assert.Null(TraceContext.Current);
        }

        [Fact]
        public void BeginScope_嵌套时内层覆盖外层()
        {
            using (TraceContext.BeginScope("outer"))
            {
                Assert.Equal("outer", TraceContext.Current);
                using (TraceContext.BeginScope("inner"))
                {
                    Assert.Equal("inner", TraceContext.Current);
                }
                Assert.Equal("outer", TraceContext.Current);
            }
            Assert.Null(TraceContext.Current);
        }
    }
}
