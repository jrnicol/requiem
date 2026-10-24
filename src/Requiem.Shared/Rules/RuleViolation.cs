namespace Requiem.Shared.Rules;

/// <summary>
/// A broken rule. Violations are warnings the GM can override, not hard errors,
/// so callers decide whether to block or just flag.
/// </summary>
public sealed record RuleViolation(string Code, string Message);
