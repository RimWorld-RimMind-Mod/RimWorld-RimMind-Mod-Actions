using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Actions.Actions
{
    /// <summary>
    /// Adapts raw <see cref="AgentDecision"/> arguments for RimMind-Actions composite tools
    /// (such as crop/food designation inference and autonomous recovery reason defaults).
    /// Registered via <see cref="CompositeToolRegistrar"/> so Core has zero hard-coded Actions heuristics.
    /// Tool IDs and aliases are derived from the composite tool definitions themselves rather than duplicated.
    /// </summary>
    public sealed class ActionsToolArgumentAdapter : IToolArgumentAdapter
    {
        private static readonly HashSet<string> ResourceDesignationIds = BuildIdSet(new DesignateResourcesCompositeTool());
        private static readonly HashSet<string> AutonomousRecoveryIds = BuildIdSet(
            new EatAndRecreationCompositeTool(),
            new StabilizeRestCompositeTool());

        private static readonly string[] FoodCropKeywords =
        {
            "berr", "food", "edible", "crop", "rice", "potato", "corn", "healroot", "agave", "ambrosia"
        };

        private static readonly string[] HuntKeywords =
        {
            "hunt", "meat", "animal"
        };

        public string Id => "actions_tool_argument_adapter";
        public string OwnerModId => RimMindOwnerConsts.ActionsModId;

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

        private static HashSet<string> BuildIdSet(params CompositeToolCallBase[] tools)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tools.Length; i++)
            {
                var tool = tools[i];
                set.Add(tool.Id);
                var aliases = tool.Manifest.Aliases;
                if (aliases != null)
                {
                    for (int j = 0; j < aliases.Count; j++)
                    {
                        if (!string.IsNullOrWhiteSpace(aliases[j]))
                            set.Add(aliases[j]);
                    }
                }
            }
            return set;
        }

        private static bool IsResourceDesignationTool(string toolId)
            => !string.IsNullOrWhiteSpace(toolId) && ResourceDesignationIds.Contains(toolId.Trim());

        private static bool IsAutonomousRecoveryTool(string toolId)
            => !string.IsNullOrWhiteSpace(toolId) && AutonomousRecoveryIds.Contains(toolId.Trim());

        private static void AdaptResourceDesignationArguments(JObject obj)
        {
            string targetStr = (obj["target"]?.ToString() ?? obj["target_name"]?.ToString() ?? string.Empty).Trim();
            bool isFoodOrCropTarget = ContainsAnyKeyword(targetStr, FoodCropKeywords);

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
                obj["designation_type"] = ContainsAnyKeyword(targetStr, HuntKeywords)
                    ? "Hunt"
                    : (isFoodOrCropTarget ? "Harvest" : "ChopWood");
            }
        }

        private static bool ContainsAnyKeyword(string input, string[] keywords)
        {
            if (string.IsNullOrEmpty(input)) return false;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (input.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
