// ActionsBridge 桥接层测试：验证空壳行为的正确性
using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Llm;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// ActionsBridge 空壳行为测试：H2 阶段后所有方法均为空操作或返回默认值
    /// </summary>
    public class ActionsBridgeTests
    {
        private readonly ActionsBridge _bridge = new();

        [Fact]
        public void Id_返回ActionsBridge()
        {
            Assert.Equal("ActionsBridge", _bridge.Id);
        }

        [Fact]
        public void OwnerModId_返回RimMindActions()
        {
            Assert.Equal("RimMindActions", _bridge.OwnerModId);
        }

        [Fact]
        public void ExecuteAction_空操作不抛异常()
        {
            var exception = Record.Exception(() => _bridge.ExecuteAction("npc1", "move_to", new[] { "100, 0" }));
            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteAction_null参数不抛异常()
        {
            var exception = Record.Exception(() => _bridge.ExecuteAction("npc1", "draft"));
            Assert.Null(exception);
        }

        [Fact]
        public void CanExecute_string重载始终返回false()
        {
            Assert.False(_bridge.CanExecute("npc1", "move_to"));
            Assert.False(_bridge.CanExecute("npc1", "draft"));
            Assert.False(_bridge.CanExecute("", ""));
        }

        [Fact]
        public void CanExecute_object重载始终返回false()
        {
            var pawn = new Verse.Pawn();
            Assert.False(_bridge.CanExecute(pawn, "move_to"));
            Assert.False(_bridge.CanExecute(pawn, "draft"));
        }

        [Fact]
        public void Execute_空操作不抛异常()
        {
            var pawn = new Verse.Pawn();
            var exception = Record.Exception(() => _bridge.Execute(pawn, "move_to", "targetName"));
            Assert.Null(exception);
        }

        [Fact]
        public void Execute_nullTarget不抛异常()
        {
            var pawn = new Verse.Pawn();
            var exception = Record.Exception(() => _bridge.Execute(pawn, "cancel_job", null));
            Assert.Null(exception);
        }

        [Fact]
        public void GetAvailableTools_始终返回null()
        {
            var pawn = new Verse.Pawn();
            List<StructuredTool>? tools = _bridge.GetAvailableTools(pawn);
            Assert.Null(tools);
        }

        [Fact]
        public void 实现IAgentActionBridge接口()
        {
            Assert.IsAssignableFrom<IAgentActionBridge>(_bridge);
        }
    }
}
