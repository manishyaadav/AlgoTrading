using StrategyService.Strategy;

namespace StrategyService.Engine
{
    // One evaluated rule. Deliberately carries the *original* TradingRule (same shape the existing
    // GetStrategy response already returns) rather than a re-described string — the dashboard's
    // Strategy page already has describeOperand()/describeRule() for turning this into readable
    // text; duplicating that formatting logic in C# too would just be two places that can drift
    // out of sync. This only adds what's new: the evaluation outcome.
    public record RuleEvaluation(
        TradingRule Rule,
        string Status,          // "pass" | "fail" | "unknown"
        string? LeftResolved,   // e.g. "24,605.41 (Up → GREEN)" — null if unresolved
        string? RightResolved,
        string? Reason);        // populated only when Status == "unknown"

    public record ValueChip(string Key, string Value, string? Tone); // Tone: "pass" | "fail" | null

    public record GateNode(
        string Eyebrow,
        string Title,
        string Status,          // "pass" | "fail" | "unknown"
        string Detail,
        List<ValueChip> Values);

    // A group of rules under one AND/OR chain (an EntryRules array, a RiskManagementRules array,
    // etc.). Live=false means the group is shown for reference only and was never evaluated —
    // used for the Exit/Stop-Loss branch today, since nothing backs the position gate it depends on.
    public record RuleGroup(
        string Title,
        string Status,
        bool Live,
        List<RuleEvaluation> Rules);

    public record EntryExitStatus(
        RuleGroup EntryRules,
        RuleGroup RiskManagementRules,
        RuleGroup ExitBranch); // UpdateStopLossRules + ExitRules combined, in that order

    public record RuleStatusResponse(
        string StrategyId,
        string StrategyName,
        string? Exchange,
        List<string> Instruments,
        string? DeployedVersion,
        GateNode DeployedGate,
        GateNode SessionGate,
        RuleGroup TradingSessionRules,
        GateNode PositionGate,
        EntryExitStatus Long,
        EntryExitStatus Short);
}
