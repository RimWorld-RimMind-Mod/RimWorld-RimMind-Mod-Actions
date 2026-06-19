using System;
using System.Linq;
using RimMind.Actions.Actions;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;
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
        public void RegisterAll_Disovers_And_Registers_StabilizeRest()
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

            Assert.All(registry.RegisteredHandlers, h => Assert.NotNull(h));
            Assert.DoesNotContain(registry.RegisteredIds, id => id == null);
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
}
