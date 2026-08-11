using StrategyService.Strategy;

namespace StrategyService.Engine
{
    // One raw field that actually went into resolving an operand — the Redis hash entries the
    // value came out of, so the evidence drawer can show the derivation rather than only the
    // answer. Values are carried exactly as stored (unrounded, unformatted): the whole point of
    // the drawer is to show what's really in Redis, so formatting it here would defeat it.
    public record EvidenceField(string Key, string Value);

    // Where one side of a comparison's value actually came from. Kind is what the rule asked for
    // ("indicator"/"literal"/"expression"), or "unresolved" when nothing in this stack backs it.
    // Numeric is populated only when the side genuinely compares as a number — it's what lets the
    // dashboard show the distance to flipping, so a null here means "don't draw a gap", never
    // "assume zero".
    public record OperandEvidence(
        string? Display,        // e.g. "24,480.34 (Down→RED)" — null if unresolved
        decimal? Numeric,
        string Kind,
        string? Source,         // the exact Redis key read; null for literals, which have no source
        string? AsOf,           // bar window / session date this value belongs to, as stored
        List<EvidenceField> Fields)
    {
        public static OperandEvidence None() => new(null, null, "unresolved", null, null, new List<EvidenceField>());
    }

    // One evaluated rule. Deliberately carries the *original* TradingRule (same shape the existing
    // GetStrategy response already returns) rather than a re-described string — the dashboard's
    // Strategy page already has describeOperand()/describeRule() for turning this into readable
    // text; duplicating that formatting logic in C# too would just be two places that can drift
    // out of sync. This only adds what's new: the evaluation outcome, and the evidence behind it.
    public record RuleEvaluation(
        TradingRule Rule,
        string Status,          // "pass" | "fail" | "unknown"
        OperandEvidence Left,
        OperandEvidence Right,
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
