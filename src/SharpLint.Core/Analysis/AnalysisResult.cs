using SharpLint.Core.Diagnostics;

namespace SharpLint.Core.Analysis;

public sealed record AnalysisResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    TimeSpan Elapsed,
    int FilesAnalyzed)
{
    public IReadOnlyList<AnalysisFailure> Failures { get; init; } = [];
    public bool IsComplete => Failures.Count == 0;
    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);
}

public sealed record AnalysisFailure(string RuleId, string Message);
