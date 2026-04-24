namespace RimMind.Actions
{
    public struct ActionResult
    {
        public bool Success { get; }
        public string Reason { get; }
        public string ActionName { get; }
        public string TargetLabel { get; }

        private ActionResult(bool success, string actionName, string reason, string targetLabel)
        {
            Success = success;
            ActionName = actionName ?? "";
            Reason = reason ?? "";
            TargetLabel = targetLabel ?? "";
        }

        public static ActionResult Succeeded(string actionName, string targetLabel = "")
            => new ActionResult(true, actionName, "", targetLabel);

        public static ActionResult Failed(string actionName, string reason, string targetLabel = "")
            => new ActionResult(false, actionName, reason, targetLabel);

        public static implicit operator bool(ActionResult result) => result.Success;

        public override string ToString()
        {
            if (Success)
                return $"[OK] {ActionName}" + (string.IsNullOrEmpty(TargetLabel) ? "" : $" → {TargetLabel}");
            return $"[FAIL] {ActionName}" + (string.IsNullOrEmpty(TargetLabel) ? "" : $" → {TargetLabel}") + $": {Reason}";
        }
    }
}
