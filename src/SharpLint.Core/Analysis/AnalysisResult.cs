using SharpLint.Core.Diagnostics;

namespace SharpLint.Core.Analysis;

public sealed record AnalysisResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    TimeSpan Elapsed,
    int FilesAnalyzed)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);
}
