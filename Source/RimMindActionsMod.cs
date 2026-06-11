using System;
using RimMind.Actions.Actions;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Api;
using UnityEngine;
using Verse;

namespace RimMind.Actions
{
    public class RimMindActionsMod : Mod
    {
        public static RimMindActionsSettings Settings { get; private set; } = null!;

        public RimMindActionsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimMindActionsSettings>();
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

        public override string SettingsCategory()
            => "RimMind.Actions.Settings.Category".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("RimMind.Actions.Settings.DeprecatedNotice".Translate());
            listing.End();
            Settings.Write();
        }
    }
}
