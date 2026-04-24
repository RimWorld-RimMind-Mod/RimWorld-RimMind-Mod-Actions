using RimWorld;
using Verse;

namespace RimMind.Actions.Actions
{
    // ─────────────────────────────────────────────
    //  trigger_incident（Critical 风险）
    // ─────────────────────────────────────────────
    public class TriggerIncidentAction : IActionRule
    {
        public string IntentId => "trigger_incident";
        public string DisplayName => "RimMind.Actions.DisplayName.TriggerIncident".Translate();
        public RiskLevel RiskLevel => RiskLevel.Critical;
        public string? ParameterSchema =>
            "{\"type\":\"object\",\"properties\":{\"param\":{\"type\":\"string\",\"description\":\"IncidentDef defName to trigger (e.g. RaidEnemy, Infestation, ManhunterPack)\"}},\"required\":[\"param\"]}";

        public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
        {
            if (string.IsNullOrEmpty(param)) return false;

            // 1. 查找 IncidentDef（找不到则静默返回）
            var incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(param!);
            if (incidentDef == null)
            {
                Log.Warning($"[RimMind-Actions] trigger_incident: unknown IncidentDef '{param}'");
                return false;
            }

            // 2. 确定目标地图
            var map = actor?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return false;

            // 3. 构建参数（点数在此计算）
            var parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
            parms.forced = true;

            // 4. 检查是否可触发
            if (!incidentDef.Worker.CanFireNow(parms)) return false;

            // 5. 执行
            return incidentDef.Worker.TryExecute(parms);
        }
    }
}
