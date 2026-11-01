namespace SharpLint.Daemon;

// The wire contract deliberately has no dependency on Roslyn or executable configuration.
public sealed record AnalysisResponse(long SnapshotVersion, ResultResponse Result, StatisticsResponse Statistics,
    string InputFingerprint, bool Validated);

public sealed record ResultResponse(IReadOnlyList<DiagnosticResponse> Diagnostics, double ElapsedMs,
    int FilesAnalyzed, IReadOnlyList<FailureResponse> Failures)
{
    public bool IsComplete => Failures.Count == 0;
    public bool HasErrors => Diagnostics.Any(d => d.Severity == "error");
}

public sealed record FailureResponse(string RuleId, string Message);

public sealed record DiagnosticResponse(string RuleId, string Origin, string? Category, string? Module,
    string Severity, string Message, string? File, int? Line, int? Column, string? Project,
    string? HelpUrl, int? SpanStart, int? SpanLength, string? Suggestion);

public sealed record StatisticsResponse(int DocumentsAnalyzed, int DocumentsReused, int CacheHits,
    int ProjectsAnalyzed, int ProjectsReused, int ProjectsInWorkspace, int CompilerDiagnostics,
    int SharpLintDiagnostics, double ReconciliationMs, double WorkspaceUpdateMs, double RuleExecutionMs,
    double AggregationMs, double TotalMs);
