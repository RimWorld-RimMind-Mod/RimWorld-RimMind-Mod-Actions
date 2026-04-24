using HarmonyLib;
using RimMind.Actions.Actions;
using RimMind.Core;
using RimMind.Core.UI;
using UnityEngine;
using Verse;

namespace RimMind.Actions
{
    public class RimMindActionsMod : Mod
    {
        public static RimMindActionsSettings Settings { get; private set; } = null!;
        private static Vector2 _scrollPos = Vector2.zero;

        public RimMindActionsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimMindActionsSettings>();
            new Harmony("mcocdaa.RimMindActions").PatchAll();
            RegisterBuiltinActions();
            RimMindAPI.RegisterAgentActionBridge(new ActionsBridge());
        }

        public override string SettingsCategory()
            => "RimMind.Actions.Settings.Category".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect contentArea = SettingsUIHelper.SplitContentArea(inRect);
            Rect bottomBar = SettingsUIHelper.SplitBottomBar(inRect);

            var intents = RimMindActionsAPI.GetSupportedIntents();
            float contentH = 60f + intents.Count * 28f + 120f;
            Rect viewRect = new Rect(0f, 0f, contentArea.width - 16f, contentH);
            Widgets.BeginScrollView(contentArea, ref _scrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            bool enabled = Settings.enableActions;
            listing.CheckboxLabeled(
                "RimMind.Actions.Settings.EnableActions".Translate(),
                ref enabled,
                "RimMind.Actions.Settings.EnableActions.Desc".Translate());
            Settings.enableActions = enabled;

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Actions.Settings.AllowedActionsLabel".Translate());
            GUI.color = Color.gray;
            listing.Label("RimMind.Actions.Settings.AllowedActionsDesc".Translate());
            GUI.color = Color.white;

            foreach (var id in intents)
            {
                var risk = RimMindActionsAPI.GetRiskLevel(id) ?? RiskLevel.Low;
                bool actionEnabled = Settings.IsAllowed(id);

                if (risk >= RiskLevel.High)
                    Widgets.DrawBoxSolidWithOutline(
                        listing.GetRect(0f), new Color(0.3f, 0f, 0f, 0.15f), Color.clear);

                string label = $"{id}   [{risk}]";
                string tooltip = GetRiskTooltip(risk);

                bool newEnabled = actionEnabled;
                listing.CheckboxLabeled(label, ref newEnabled, tooltip);

                if (newEnabled != actionEnabled)
                {
                    if (newEnabled)
                        Settings.DisabledIntents.Remove(id);
                    else
                        Settings.DisabledIntents.Add(id);
                }
            }

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Actions.Settings.QueueSection".Translate());

            float maxSize = Settings.delayedQueueMaxSize;
            listing.Label("RimMind.Actions.Settings.QueueMaxSize".Translate() + $": {maxSize}");
            maxSize = listing.Slider(maxSize, 10f, 200f);
            Settings.delayedQueueMaxSize = (int)maxSize;

            float defaultDelay = Settings.delayedQueueDefaultDelay;
            listing.Label("RimMind.Actions.Settings.DefaultDelay".Translate() + $": {defaultDelay:F1}");
            defaultDelay = listing.Slider(defaultDelay, 0.5f, 10f);
            Settings.delayedQueueDefaultDelay = defaultDelay;

            listing.End();
            Widgets.EndScrollView();

            DrawBottomBar(bottomBar);

            Settings.Write();
        }

        private static void DrawBottomBar(Rect barRect)
        {
            float btnW = 160f;
            float btnH = 30f;
            float btnY = barRect.y + (barRect.height - btnH) / 2f;
            float x = barRect.x;

            Rect highBtn = new Rect(x, btnY, btnW, btnH);
            if (Widgets.ButtonText(highBtn, "RimMind.Actions.Settings.DisableHighRisk".Translate()))
            {
                foreach (var id in RimMindActionsAPI.GetSupportedIntents())
                {
                    var risk = RimMindActionsAPI.GetRiskLevel(id) ?? RiskLevel.Low;
                    if (risk >= RiskLevel.High)
                        Settings.DisabledIntents.Add(id);
                }
            }
            x += btnW + 8f;

            Rect criticalBtn = new Rect(x, btnY, btnW, btnH);
            if (Widgets.ButtonText(criticalBtn, "RimMind.Actions.Settings.DisableCritical".Translate()))
            {
                foreach (var id in RimMindActionsAPI.GetSupportedIntents())
                {
                    var risk = RimMindActionsAPI.GetRiskLevel(id) ?? RiskLevel.Low;
                    if (risk >= RiskLevel.Critical)
                        Settings.DisabledIntents.Add(id);
                }
            }
            x += btnW + 8f;

            Rect resetBtn = new Rect(x, btnY, 120f, btnH);
            if (Widgets.ButtonText(resetBtn, "RimMind.Actions.Settings.ResetToDefault".Translate()))
            {
                Settings.DisabledIntents.Clear();
                Settings.enableActions = true;
                Settings.delayedQueueMaxSize = 50;
                Settings.delayedQueueDefaultDelay = 1.5f;
            }
        }

        private static string GetRiskTooltip(RiskLevel risk) => risk switch
        {
            RiskLevel.Low => "RimMind.Actions.UI.Risk.Low".Translate(),
            RiskLevel.Medium => "RimMind.Actions.UI.Risk.Medium".Translate(),
            RiskLevel.High => "RimMind.Actions.UI.Risk.High".Translate(),
            RiskLevel.Critical => "RimMind.Actions.UI.Risk.Critical".Translate(),
            _ => ""
        };

        private static void RegisterBuiltinActions()
        {
            RimMindActionsAPI.RegisterAction("force_rest", new ForceRestAction());
            RimMindActionsAPI.RegisterAction("assign_work", new AssignWorkAction());
            RimMindActionsAPI.RegisterAction("move_to", new MoveToAction());
            RimMindActionsAPI.RegisterAction("eat_food", new EatFoodAction());
            RimMindActionsAPI.RegisterAction("draft", new DraftAction());
            RimMindActionsAPI.RegisterAction("undraft", new UndraftAction());
            RimMindActionsAPI.RegisterAction("tend_pawn", new TendPawnAction());
            RimMindActionsAPI.RegisterAction("rescue_pawn", new RescuePawnAction());
            RimMindActionsAPI.RegisterAction("arrest_pawn", new ArrestPawnAction());
            RimMindActionsAPI.RegisterAction("cancel_job", new CancelJobAction());
            RimMindActionsAPI.RegisterAction("set_work_priority", new SetWorkPriorityAction());
            RimMindActionsAPI.RegisterAction("drop_weapon", new DropWeaponAction());

            RimMindActionsAPI.RegisterAction("social_dining", new SocialDiningAction());
            RimMindActionsAPI.RegisterAction("social_relax", new SocialRelaxAction());
            RimMindActionsAPI.RegisterAction("give_item", new GiveItemAction());
            RimMindActionsAPI.RegisterAction("romance_accept", new RomanceAcceptAction());
            RimMindActionsAPI.RegisterAction("romance_breakup", new RomanceBreakupAction());

            RimMindActionsAPI.RegisterAction("recruit_agree", new RecruitAgreeAction());
            RimMindActionsAPI.RegisterAction("adjust_faction", new AdjustFactionAction());

            RimMindActionsAPI.RegisterAction("inspire_work", new InspireWorkAction());
            RimMindActionsAPI.RegisterAction("inspire_fight", new InspireFightAction());
            RimMindActionsAPI.RegisterAction("inspire_trade", new InspireTradeAction());
            RimMindActionsAPI.RegisterAction("add_thought", new AddThoughtAction());
            RimMindActionsAPI.RegisterAction("trigger_mental_state", new TriggerMentalStateAction());

            RimMindActionsAPI.RegisterAction("trigger_incident", new TriggerIncidentAction());
        }
    }
}
