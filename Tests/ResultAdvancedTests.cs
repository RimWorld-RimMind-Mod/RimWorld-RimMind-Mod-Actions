// Result 高级功能测试：验证 Match、Map、TryGetValue、TryGetError
using System;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// Result<TValue, TError> 高级功能测试：补充现有 ActionResultTests 未覆盖的
    /// Match、Map、TryGetValue、TryGetError 等方法
    /// </summary>
    public class ResultAdvancedTests
    {
        [Fact]
        public void Match_Ok分支执行onOk()
        {
            var result = Result<string, RimMindError>.Ok("成功");
            var matched = result.Match(
                onOk: v => $"值: {v}",
                onErr: e => $"错误: {e.Message}"
            );
            Assert.Equal("值: 成功", matched);
        }

        [Fact]
        public void Match_Err分支执行onErr()
        {
            var error = new RimMindError(RimMindErrorCode.ToolNotFound, "工具不存在");
            var result = Result<string, RimMindError>.Err(error);
            var matched = result.Match(
                onOk: v => $"值: {v}",
                onErr: e => $"错误: {e.Message}"
            );
            Assert.Equal("错误: 工具不存在", matched);
        }

        [Fact]
        public void Map_Ok时转换值()
        {
            var result = Result<int, RimMindError>.Ok(42);
            var mapped = result.Map(v => v.ToString());
            Assert.True(mapped.IsOk);
            Assert.Equal("42", mapped.Value);
        }

        [Fact]
        public void Map_Err时保留错误()
        {
            var error = new RimMindError(RimMindErrorCode.Cancelled, "已取消");
            var result = Result<int, RimMindError>.Err(error);
            var mapped = result.Map(v => v.ToString());
            Assert.True(mapped.IsErr);
            Assert.Equal(RimMindErrorCode.Cancelled, mapped.Error.Code);
        }

        [Fact]
        public void TryGetValue_Ok时返回true并输出值()
        {
            var result = Result<string, RimMindError>.Ok("测试值");
            Assert.True(result.TryGetValue(out var value));
            Assert.Equal("测试值", value);
        }

        [Fact]
        public void TryGetValue_Err时返回false()
        {
            var error = new RimMindError(RimMindErrorCode.InternalError, "错误");
            var result = Result<string, RimMindError>.Err(error);
            Assert.False(result.TryGetValue(out var value));
            Assert.Null(value);
        }

        [Fact]
        public void TryGetError_Err时返回true并输出错误()
        {
            var error = new RimMindError(RimMindErrorCode.Timeout, "超时");
            var result = Result<string, RimMindError>.Err(error);
            Assert.True(result.TryGetError(out var err));
            Assert.Equal(RimMindErrorCode.Timeout, err!.Code);
        }

        [Fact]
        public void TryGetError_Ok时返回false()
        {
            var result = Result<string, RimMindError>.Ok("成功");
            Assert.False(result.TryGetError(out var err));
            Assert.Null(err);
        }

        [Fact]
        public void Value_Err时访问抛出InvalidOperationException()
        {
            var error = new RimMindError(RimMindErrorCode.NotImplemented, "未实现");
            var result = Result<string, RimMindError>.Err(error);
            Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        }

        [Fact]
        public void Error_Ok时访问抛出InvalidOperationException()
        {
            var result = Result<string, RimMindError>.Ok("成功");
            Assert.Throws<InvalidOperationException>(() => _ = result.Error);
        }
    }
}
