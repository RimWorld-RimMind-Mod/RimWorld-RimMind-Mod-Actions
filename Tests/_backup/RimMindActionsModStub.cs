// RimMindActionsMod 桩：提供 Settings 静态属性，供 ActionsSettingsValidator 引用
using System;
using Verse;

namespace RimMind.Actions
{
    [Obsolete("RimMindActionsMod is deprecated.")]
    public class RimMindActionsMod : Mod
    {
        public static RimMindActionsSettings Settings { get; private set; } = null!;

        public RimMindActionsMod(ModContentPack content) : base(content) { }
    }
}
