namespace SharpLint.Core.Diagnostics;

public sealed record Diagnostic(
    string RuleId,
    Severity Severity,
    string Message,
    SourceLocation Location,
    string? Suggestion = null)
{
    /// <summary>The canonical category ID, when supplied by the analysis host.</summary>
    public string? CategoryId { get; init; }
}
