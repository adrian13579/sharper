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

    public DiagnosticOrigin Origin { get; init; } = DiagnosticOrigin.SharpLint;

    /// <summary>Stable project context (project path plus target framework/name), if applicable.</summary>
    public string? Project { get; init; }

    public string? HelpUrl { get; init; }

    /// <summary>False for compilation-wide diagnostics; Location is default in that case.</summary>
    public bool HasSourceLocation { get; init; } = true;

    /// <summary>Physical, zero-based UTF-16 offset and length, when provided by Roslyn.</summary>
    public int? SpanStart { get; init; }
    public int? SpanLength { get; init; }
}
