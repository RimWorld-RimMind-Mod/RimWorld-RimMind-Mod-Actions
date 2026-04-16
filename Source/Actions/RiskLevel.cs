namespace RimMind.Actions
{
    public enum RiskLevel
    {
        Low,      // 无副作用，可随时撤销（e.g. move_to, cancel_job）
        Medium,   // 有轻微副作用（e.g. social_relax, add_thought）
        High,     // 重大行为改变（e.g. recruit_agree, arrest_pawn, drop_weapon）
        Critical  // 不可逆或影响全局（e.g. trigger_mental_state, trigger_incident）
    }
}
