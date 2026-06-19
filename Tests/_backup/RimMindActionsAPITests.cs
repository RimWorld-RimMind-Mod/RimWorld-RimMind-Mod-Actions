// RimMindActionsAPI 废弃 API 测试：验证所有方法均为空操作或返回默认值
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// RimMindActionsAPI 废弃静态 API 测试：H2 后所有方法转发至 Core Mechanisms，
    /// 本模块方法均为空操作。验证合约一致性。
    /// </summary>
    public class RimMindActionsAPITests
    {
        [Fact]
        public void Execute_始终返回false()
        {
            var pawn = new Verse.Pawn();
            Assert.False(RimMindActionsAPI.Execute("move_to", pawn));
            Assert.False(RimMindActionsAPI.Execute("draft", pawn, null, null));
        }

        [Fact]
        public void ExecuteWithResult_始终返回false()
        {
            var pawn = new Verse.Pawn();
            Assert.False(RimMindActionsAPI.ExecuteWithResult("move_to", pawn));
        }

        [Fact]
        public void ExecuteBatch_始终返回0()
        {
            var intents = new List<BatchActionIntent>
            {
                new() { IntentId = "move_to", Actor = new Verse.Pawn() }
            };
            Assert.Equal(0, RimMindActionsAPI.ExecuteBatch(intents));
        }

        [Fact]
        public void ExecuteBatch_空列表也返回0()
        {
            Assert.Equal(0, RimMindActionsAPI.ExecuteBatch(new List<BatchActionIntent>()));
        }

        [Fact]
        public void ExecuteBatchWithResults_始终返回空列表()
        {
            var intents = new List<BatchActionIntent>
            {
                new() { IntentId = "move_to", Actor = new Verse.Pawn() }
            };
            var results = RimMindActionsAPI.ExecuteBatchWithResults(intents);
            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GetSupportedIntents_始终返回空集合()
        {
            var intents = RimMindActionsAPI.GetSupportedIntents();
            Assert.NotNull(intents);
            Assert.Empty(intents);
        }

        [Fact]
        public void GetActionDescriptions_始终返回空集合()
        {
            var descriptions = RimMindActionsAPI.GetActionDescriptions();
            Assert.NotNull(descriptions);
            Assert.Empty(descriptions);
        }

        [Fact]
        public void GetActionListText_返回废弃提示()
        {
            var text = RimMindActionsAPI.GetActionListText();
            Assert.Contains("deprecated", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("RimMindAPI.Tools", text);
        }

        [Fact]
        public void IsAllowed_始终返回false()
        {
            Assert.False(RimMindActionsAPI.IsAllowed("move_to"));
            Assert.False(RimMindActionsAPI.IsAllowed("draft"));
            Assert.False(RimMindActionsAPI.IsAllowed(""));
        }

        [Fact]
        public void GetRiskLevel_始终返回null()
        {
            Assert.Null(RimMindActionsAPI.GetRiskLevel("move_to"));
            Assert.Null(RimMindActionsAPI.GetRiskLevel("trigger_incident"));
        }

        [Fact]
        public void GetWorkTargets_始终返回空列表()
        {
            var pawn = new Verse.Pawn();
            var targets = RimMindActionsAPI.GetWorkTargets(pawn, "Mining", 10);
            Assert.NotNull(targets);
            Assert.Empty(targets);
        }

        [Fact]
        public void GetActionHintData_始终返回null()
        {
            var pawn = new Verse.Pawn();
            Assert.Null(RimMindActionsAPI.GetActionHintData(pawn, "eat_food"));
        }

        [Fact]
        public void RegisterAction_空操作不抛异常()
        {
            var exception = Record.Exception(() => RimMindActionsAPI.RegisterAction("test_intent", new object()));
            Assert.Null(exception);
        }
    }
}
