using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Actions.Actions
{
    // ─────────────────────────────────────────────
    //  social_dining
    //  注：FoodSharingUtility 是 RimTalk-ExpandActions 中的自定义工具类，
    //  此处使用原生 TryInteractWith 替代，触发 ShareMeal 或 ChatFriendly 互动。
    // ─────────────────────────────────────────────
    public class SocialDiningAction : IActionRule
    {
        public string IntentId    => "social_dining";
        public string DisplayName => "RimMind.Actions.DisplayName.SocialDining".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;

            // 尝试 ShareMeal 互动（如存在），fallback 到 ChatFriendly
            var intDef = DefDatabase<InteractionDef>.GetNamed("ShareMeal", false)
                      ?? DefDatabase<InteractionDef>.GetNamed("ChatFriendly", false);
            if (intDef == null) return false;

            if (!actor.interactions.CanInteractNowWith(target, intDef)) return false;
            actor.interactions.TryInteractWith(target, intDef);

            // 给双方触发 Catharsis（释放压力）以模拟聚餐心情效果
            actor.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Catharsis);
            target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Catharsis);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  social_relax
    // ─────────────────────────────────────────────
    public class SocialRelaxAction : IActionRule
    {
        public string IntentId    => "social_relax";
        public string DisplayName => "RimMind.Actions.DisplayName.SocialRelax".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (actor.needs?.mood?.thoughts?.memories == null) return false;

            // TryGainMemory 接受 ThoughtDef（不需要 ThoughtMaker.MakeThought 包装）
            actor.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.Catharsis);

            if (actor.timetable != null)
                actor.timetable.SetAssignment(GenLocalDate.HourOfDay(actor), TimeAssignmentDefOf.Joy);

            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  give_item
    // ─────────────────────────────────────────────
    public class GiveItemAction : IActionRule
    {
        public string IntentId    => "give_item";
        public string DisplayName => "RimMind.Actions.DisplayName.GiveItem".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;

        /// <summary>
        /// param：物品关键词（大小写不敏感，匹配 Label 或 defName）。
        /// target：受赠的小人（必填）— 物品掉落在 target 附近，而非 actor 脚下。
        ///          target 为空时掉落在 actor 脚下作为兜底。
        /// </summary>
        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;
            if (actor.inventory?.innerContainer == null) return false;

            var keyword = param!.ToLowerInvariant();
            Thing? found = null;
            foreach (var thing in actor.inventory.innerContainer)
            {
                if (thing.Label.ToLowerInvariant().Contains(keyword) ||
                    thing.def.defName.ToLowerInvariant().Contains(keyword))
                {
                    found = thing;
                    break;
                }
            }
            if (found == null) return false;

            // 掉落在受赠者附近（而非赠送者脚下），让受赠者能捡到
            var dropPos = target?.Position ?? actor.Position;
            actor.inventory.innerContainer.TryDrop(
                found, dropPos, actor.Map, ThingPlaceMode.Near, out _);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  romance_accept
    // ─────────────────────────────────────────────
    public class RomanceAcceptAction : IActionRule
    {
        public string IntentId    => "romance_accept";
        public string DisplayName => "RimMind.Actions.DisplayName.RomanceAccept".Translate();
        public RiskLevel RiskLevel => RiskLevel.Medium;
        public bool IsJobBased => true;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;

            var intDef = DefDatabase<InteractionDef>.GetNamed("RomanceAttempt", false);
            if (intDef == null) return false;

            if (actor.interactions.CanInteractNowWith(target, intDef))
            {
                actor.interactions.TryInteractWith(target, intDef);
                return true;
            }

            // 距离过远：先移过去
            actor.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(JobDefOf.Goto, target.Position),
                JobTag.Misc);
            return true;
        }
    }

    // ─────────────────────────────────────────────
    //  romance_breakup
    // ─────────────────────────────────────────────
    public class RomanceBreakupAction : IActionRule
    {
        public string IntentId    => "romance_breakup";
        public string DisplayName => "RimMind.Actions.DisplayName.RomanceBreakup".Translate();
        public RiskLevel RiskLevel => RiskLevel.High;
        public bool IsJobBased => true;

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (target == null) return false;

            var intDef = DefDatabase<InteractionDef>.GetNamed("Breakup", false);
            if (intDef == null) return false;

            if (actor.interactions.CanInteractNowWith(target, intDef))
            {
                actor.interactions.TryInteractWith(target, intDef);
                return true;
            }

            actor.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(JobDefOf.Goto, target.Position),
                JobTag.Misc);
            return true;
        }
    }
}
