using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class CompositeToolRegistrarTests
    {
        [Fact]
        public void RegisterAll_NullRegistry_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CompositeToolRegistrar.RegisterAll(null!, typeof(StabilizeRestCompositeTool).Assembly));
        }

        [Fact]
        public void RegisterAll_NullAssembly_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CompositeToolRegistrar.RegisterAll(new FakeRegistry(), null!));
        }

        [Fact]
        public void RegisterAll_Discovers_And_Registers_StabilizeRest()
        {
            var registry = new FakeRegistry();
            CompositeToolRegistrar.RegisterAll(registry, typeof(StabilizeRestCompositeTool).Assembly);

            Assert.Contains("actions.stabilize_rest", registry.RegisteredIds);
        }

        [Fact]
        public void RegisterAll_Does_Not_Register_Abstract_Base()
        {
            var registry = new FakeRegistry();
            CompositeToolRegistrar.RegisterAll(registry, typeof(StabilizeRestCompositeTool).Assembly);

            Assert.Single(registry.RegisteredHandlers);
            Assert.Equal("actions.stabilize_rest", registry.RegisteredIds.Single());
        }

        [Fact]
        public void RegisterAll_Skips_Type_With_No_Parameterless_Constructor_And_Continues()
        {
            var registry = new FakeRegistry();
            // Test assembly contains BadCompositeToolNoCtor (public, no paramless ctor)
            // and StabilizeRestCompositeTool (compiled via <Compile Include>)
            CompositeToolRegistrar.RegisterAll(registry, typeof(BadCompositeToolNoCtor).Assembly);

            // Valid tool should still be registered
            Assert.Contains("actions.stabilize_rest", registry.RegisteredIds);
            // Bad tool should NOT be registered
            Assert.DoesNotContain("test.bad_no_ctor", registry.RegisteredIds);
        }

        [Fact]
        public void RegisterAll_Type_Without_Parameterless_Constructor_Logs_Via_Verse_Log()
        {
            Verse.Log.Messages.Clear();
            var registry = new FakeRegistry();
            CompositeToolRegistrar.RegisterAll(registry, typeof(BadCompositeToolNoCtor).Assembly);

            Assert.Contains(Verse.Log.Messages, m => m.Contains("Skipped") && m.Contains("BadCompositeToolNoCtor"));
        }

        [Fact]
        public void RegisterAll_Duplicate_Id_Logs_Warning_And_Skips_Second()
        {
            Verse.Log.Messages.Clear();
            var registry = new FakeRegistry();
            // RegisterAll scans the test assembly which contains both StabilizeRestCompositeTool
            // and DuplicateIdCompositeTool — both declare Id "actions.stabilize_rest".
            CompositeToolRegistrar.RegisterAll(registry, typeof(DuplicateIdCompositeTool).Assembly);

            Assert.Single(registry.RegisteredHandlers);
            Assert.Contains(Verse.Log.Messages, m => m.Contains("duplicate Id") && m.Contains("actions.stabilize_rest"));
        }

        private sealed class FakeRegistry : IToolRegistry
        {
            private readonly System.Collections.Generic.List<IToolHandler> _handlers = new();

            public System.Collections.Generic.IReadOnlyList<string> RegisteredIds =>
                _handlers.Select(h => h.Id).ToList();

            public System.Collections.Generic.IReadOnlyList<IToolHandler> RegisteredHandlers => _handlers;

            public System.Collections.Generic.IReadOnlyList<IToolHandler> All => _handlers;

            public IToolHandler? FindById(string toolId) =>
                _handlers.FirstOrDefault(h => h.Id == toolId);

            public void Register(IToolHandler handler) => _handlers.Add(handler);

            public bool Unregister(string toolId) =>
                _handlers.RemoveAll(h => h.Id == toolId) > 0;

            public System.Collections.Generic.IReadOnlyList<ToolDefinition> GetAllDefinitions() =>
                _handlers.Select(h => h.Definition).ToList();

            public System.Collections.Generic.IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind) =>
                _handlers.ToList();

            public System.Collections.Generic.IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind) =>
                _handlers.Select(h => h.Definition).ToList();
        }
    }

    /// <summary>
    /// Test-only composite tool with no parameterless constructor.
    /// Used to verify CompositeToolRegistrar skips it gracefully.
    /// </summary>
    public sealed class BadCompositeToolNoCtor : CompositeToolCallBase
    {
        public BadCompositeToolNoCtor(int unused) { }

        public override string Id => "test.bad_no_ctor";
        public override ToolDefinition Definition => new ToolDefinition { Id = Id };
        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();
        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// Test-only composite tool with same Id as StabilizeRestCompositeTool.
    /// Used to verify duplicate Id detection.
    /// </summary>
    public sealed class DuplicateIdCompositeTool : CompositeToolCallBase
    {
        public override string Id => "actions.stabilize_rest";
        public override ToolDefinition Definition => new ToolDefinition { Id = Id };
        public override IReadOnlyList<string> RequiredToolIds => Array.Empty<string>();
        public override Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct) =>
            throw new NotImplementedException();
    }
}
