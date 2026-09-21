using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Tests
{
    internal sealed class FakeToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, IToolHandler> _handlers = new();
        private readonly List<string> _lookupOrder = new();

        public IReadOnlyList<string> LookupOrder => _lookupOrder;

        public IReadOnlyList<IToolHandler> All => _handlers.Values.ToList();

        public void Register(IToolHandler handler) => _handlers[handler.Id] = handler;

        public bool Unregister(string toolId) => _handlers.Remove(toolId);

        public IToolHandler? FindById(string toolId)
        {
            _lookupOrder.Add(toolId);
            return _handlers.TryGetValue(toolId, out var handler) ? handler : null;
        }

        public IReadOnlyList<ToolDefinition> GetAllDefinitions() =>
            _handlers.Values.Select(h => h.Definition).ToList();

        public IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind) =>
            _handlers.Values.ToList();

        public IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind) =>
            _handlers.Values.Select(h => h.Definition).ToList();
    }

    internal sealed class RecordingHandler : IToolHandler
    {
        private readonly ToolResult _result;

        public RecordingHandler(string id, ToolResult result)
        {
            Id = id;
            _result = result;
        }

        public string Id { get; }

        public string OwnerModId => "RimMindActions.Tests";

        public ToolDefinition Definition => new ToolDefinition { Id = Id };

        public ToolCallArgs? ReceivedArgs { get; private set; }

        public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
        {
            ReceivedArgs = args;
            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(_result));
        }
    }
}
