using System.Collections.Generic;
using Verse;

namespace RimMind.Actions
{
    /// <summary>
    /// Mod 设置。通过 Verse.ModSettings 持久化，跨存档/重启保留。
    /// </summary>
    public class RimMindActionsSettings : ModSettings
    {
        /// <summary>
        /// 被玩家禁用的意图 ID 集合（空 = 全部允许）。
        /// 存储字符串，对应 mod 未加载时保留该条目，不报错。
        /// </summary>
        public HashSet<string> DisabledIntents = new HashSet<string>();
        public bool enableActions = true;
        public int delayedQueueMaxSize = 50;
        public float delayedQueueDefaultDelay = 1.5f;

        /// <summary>
        /// 检查指定意图是否被玩家允许执行。
        /// </summary>
        public bool IsAllowed(string intentId)
            => !DisabledIntents.Contains(intentId);

        public override void ExposeData()
        {
            // HashSet 需先转为 List 才能被 Scribe_Collections.Look 序列化
            var list = new List<string>(DisabledIntents);
            Scribe_Collections.Look(ref list, "disabledIntents", LookMode.Value);
            DisabledIntents = list != null
                ? new HashSet<string>(list)
                : new HashSet<string>();
            Scribe_Values.Look(ref enableActions, "enableActions", true);
            Scribe_Values.Look(ref delayedQueueMaxSize, "delayedQueueMaxSize", 50);
            Scribe_Values.Look(ref delayedQueueDefaultDelay, "delayedQueueDefaultDelay", 1.5f);
        }
    }

    /// <summary>
    /// 在所有 Mod 构造函数执行完毕（所有动作已注册）后，
    /// 检查设置中是否存在"孤儿"意图 ID（对应 mod 未加载），并输出警告。
    /// 孤儿条目被保留——重新加载对应 mod 后设置自动恢复生效。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ActionsSettingsValidator
    {
        static ActionsSettingsValidator()
        {
            var settings = RimMindActionsMod.Settings;
            if (settings == null || settings.DisabledIntents.Count == 0) return;

            var registered = new HashSet<string>(RimMindActionsAPI.GetSupportedIntents());
            foreach (var id in settings.DisabledIntents)
            {
                if (!registered.Contains(id))
                {
                    Log.Warning(
                        $"[RimMind-Actions] Settings contain unregistered intent '{id}', " +
                        $"the corresponding mod may not be loaded or has been removed. Entry preserved - will auto-activate when the mod is re-enabled.");
                }
            }
        }
    }
}
