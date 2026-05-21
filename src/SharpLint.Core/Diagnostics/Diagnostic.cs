namespace SharpLint.Core.Diagnostics;

public sealed record Diagnostic(
    string RuleId,
    Severity Severity,
    string Message,
    SourceLocation Location,
    string? Suggestion = null);
