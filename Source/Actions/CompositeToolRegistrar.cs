using System;
using System.Linq;
using System.Reflection;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.ValueObjects;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Discovers all public, non-abstract <see cref="CompositeToolCallBase"/> subclasses in an
    /// assembly and registers them with the tool registry. New composite tools are picked up
    /// automatically — no need to edit registration code per tool (open/closed principle).
    /// </summary>
    public static class CompositeToolRegistrar
    {
        public static void RegisterAll(IToolRegistry registry, Assembly assembly)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var toolTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic
                    && typeof(CompositeToolCallBase).IsAssignableFrom(t));

            foreach (var type in toolTypes)
            {
                try
                {
                    var tool = (CompositeToolCallBase)Activator.CreateInstance(type)!;
                    registry.Register(tool);
                }
                catch (MissingMethodException)
                {
                    RimMindErrors.Warn($"[RimMind-Actions] Skipped {type.FullName}: no parameterless constructor");
                }
                catch (Exception ex)
                {
                    RimMindErrors.Warn($"[RimMind-Actions] Skipped {type.FullName}: {ex.Message}", ex);
                }
            }
        }
    }
}
