using System;
using HarmonyLib;
using RimMind.Application.Common.Interfaces.Extension;
using UnityEngine;
using Verse;

namespace RimMind.Actions
{
    [Obsolete("RimMindActionsMod is deprecated. All functionality has been migrated to RimMind-Core Mechanisms.")]
    public class RimMindActionsMod : Mod
    {
        public static RimMindActionsSettings Settings { get; private set; } = null!;

        public RimMindActionsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimMindActionsSettings>();
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
