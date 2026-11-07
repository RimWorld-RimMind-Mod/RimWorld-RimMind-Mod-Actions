using System;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Adapts raw <see cref="AgentDecision"/> arguments for RimMind-Actions composite tools
    /// (such as crop/food designation inference and autonomous recovery reason defaults).
    /// Registered via <see cref="CompositeToolRegistrar"/> so Core has zero hard-coded Actions heuristics.
    /// </summary>
    public sealed class ActionsToolArgumentAdapter : IToolArgumentAdapter
    {
        public string Id => "actions_tool_argument_adapter";
        public string OwnerModId => "RimMindActions";

        public bool CanAdapt(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId)) return false;
            return IsResourceDesignationTool(toolId) || IsAutonomousRecoveryTool(toolId);
        }

        public JObject AdaptArguments(string toolId, JObject args, AgentDecision decision, int callerPawnId)
        {
            var obj = args ?? new JObject();

            if (IsResourceDesignationTool(toolId) ||
                (!string.IsNullOrWhiteSpace(decision?.ActionIntent) && IsResourceDesignationTool(decision!.ActionIntent)))
            {
                AdaptResourceDesignationArguments(obj);
            }
            else if (IsAutonomousRecoveryTool(toolId) ||
                     (!string.IsNullOrWhiteSpace(decision?.ActionIntent) && IsAutonomousRecoveryTool(decision!.ActionIntent)))
            {
                if (!obj.ContainsKey("reason"))
                {
                    obj["reason"] = !string.IsNullOrEmpty(decision?.Reason) ? decision!.Reason : "autonomous recovery";
                }
            }

            return obj;
        }

        private static bool IsResourceDesignationTool(string toolId)
            => string.Equals(toolId, "actions_designate_resources", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "actions.designate_resources", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "designate_resources", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "designate_map_order", StringComparison.OrdinalIgnoreCase);

        private static bool IsAutonomousRecoveryTool(string toolId)
            => string.Equals(toolId, "actions.eat_and_recreation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "actions_eat_and_recreation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "eat_and_recreation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "calm_recreation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "actions.stabilize_rest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "actions_stabilize_rest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "stabilize_rest", StringComparison.OrdinalIgnoreCase);

        private static void AdaptResourceDesignationArguments(JObject obj)
        {
            string targetStr = (obj["target"]?.ToString() ?? obj["target_name"]?.ToString() ?? "").Trim();
            bool isFoodOrCropTarget =
                targetStr.IndexOf("berr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("food", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("edible", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("crop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("rice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("potato", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("corn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("healroot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("agave", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetStr.IndexOf("ambrosia", StringComparison.OrdinalIgnoreCase) >= 0;

            if (obj.TryGetValue("order_type", StringComparison.OrdinalIgnoreCase, out var otVal))
            {
                string ot = otVal.ToString();
                if (ot.Equals("CutPlants", StringComparison.OrdinalIgnoreCase))
                    obj["designation_type"] = isFoodOrCropTarget ? "Harvest" : "ChopWood";
                else if (ot.Equals("ChopWood", StringComparison.OrdinalIgnoreCase))
                    obj["designation_type"] = "ChopWood";
                else if (ot.Equals("Harvest", StringComparison.OrdinalIgnoreCase))
                    obj["designation_type"] = "Harvest";
                else if (ot.Equals("Mine", StringComparison.OrdinalIgnoreCase))
                    obj["designation_type"] = "Mine";
                else
                    obj["designation_type"] = ot;
            }
            else if (!obj.ContainsKey("designation_type"))
            {
                obj["designation_type"] = isFoodOrCropTarget ? "Harvest" : "ChopWood";
            }
        }
    }
}
