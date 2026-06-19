using System;
using RimMind.Actions.Actions;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Actions
{
    public class RimMindActionsMod : Mod
    {
        public RimMindActionsMod(ModContentPack content) : base(content)
        {
            LongEventHandler.ExecuteWhenFinished(RegisterCompositeTools);
        }

        private static void RegisterCompositeTools()
        {
            try
            {
                RimMindAPI.Tools.Register(new StabilizeRestCompositeTool());
                Log.Message("[RimMind-Actions] Registered composite tool: actions.stabilize_rest");
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Actions] Failed to register composite tools: {ex.Message}");
            }
        }
    }
}
