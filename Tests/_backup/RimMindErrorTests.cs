// RimMindError 值对象测试：验证构造、格式化和 record 相等性
using System;
using System.Collections.Generic;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// RimMindError record 测试：验证构造函数、ToString 格式、可选属性默认值和 record 相等性
    /// </summary>
    public class RimMindErrorTests
    {
        [Fact]
        public void 构造函数设置Code和Message()
        {
            var error = new RimMindError(RimMindErrorCode.ToolNotFound, "工具未找到");
            Assert.Equal(RimMindErrorCode.ToolNotFound, error.Code);
            Assert.Equal("工具未找到", error.Message);
        }

        [Fact]
        public void 可选属性默认为null()
        {
            var error = new RimMindError(RimMindErrorCode.InternalError, "内部错误");
            Assert.Null(error.TraceId);
            Assert.Null(error.Source);
            Assert.Null(error.Details);
            Assert.Null(error.InnerException);
        }

        [Fact]
        public void ToString_无TraceId时仅显示Code和Message()
        {
            var error = new RimMindError(RimMindErrorCode.Cancelled, "已取消");
            Assert.Equal("[Cancelled] 已取消", error.ToString());
        }

        [Fact]
        public void ToString_有TraceId时追加trace信息()
        {
            var error = new RimMindError(RimMindErrorCode.Timeout, "超时")
            {
                TraceId = "trace-123"
            };
            var text = error.ToString();
            Assert.Contains("[Timeout]", text);
            Assert.Contains("超时", text);
            Assert.Contains("trace=trace-123", text);
        }

        [Fact]
        public void Record相等性_相同Code和Message视为相等()
        {
            var error1 = new RimMindError(RimMindErrorCode.ToolNotFound, "未找到");
            var error2 = new RimMindError(RimMindErrorCode.ToolNotFound, "未找到");
            Assert.Equal(error1, error2);
            Assert.Equal(error1.GetHashCode(), error2.GetHashCode());
        }

        [Fact]
        public void Record相等性_不同Code视为不等()
        {
            var error1 = new RimMindError(RimMindErrorCode.ToolNotFound, "未找到");
            var error2 = new RimMindError(RimMindErrorCode.ToolExecutionFailed, "未找到");
            Assert.NotEqual(error1, error2);
        }

        [Fact]
        public void Details可赋值并读取()
        {
            var details = new Dictionary<string, object?>
            {
                ["tool_id"] = "move_to",
                ["depth"] = 3
            };
            var error = new RimMindError(RimMindErrorCode.ToolMaxDepthExceeded, "递归超限")
            {
                Details = details
            };
            Assert.NotNull(error.Details);
            Assert.Equal("move_to", error.Details!["tool_id"]);
            Assert.Equal(3, error.Details!["depth"]);
        }

        [Fact]
        public void InnerException可赋值并读取()
        {
            var inner = new InvalidOperationException("内部异常");
            var error = new RimMindError(RimMindErrorCode.InternalError, "内部错误")
            {
                InnerException = inner
            };
            Assert.Same(inner, error.InnerException);
        }
    }
}
