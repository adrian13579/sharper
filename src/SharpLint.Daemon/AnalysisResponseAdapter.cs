using SharpLint.Analysis;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Daemon;

internal static class AnalysisResponseAdapter
{
    public static AnalysisResponse FromSnapshot(SnapshotResult snapshot)
    {
        var result = snapshot.Result;
        var stats = snapshot.Statistics;
        return new AnalysisResponse(snapshot.SnapshotVersion,
            new ResultResponse(result.Diagnostics.Select(d => new DiagnosticResponse(d.RuleId,
                d.Origin == DiagnosticOrigin.Compiler ? "compiler" : "sharplint", d.CategoryId, d.ModuleId,
                d.Severity.ToString().ToLowerInvariant(), d.Message,
                d.HasSourceLocation ? d.Location.FilePath : null,
                d.HasSourceLocation ? d.Location.Line : null,
                d.HasSourceLocation ? d.Location.Column : null,
                d.Project, d.HelpUrl, d.SpanStart, d.SpanLength, d.Suggestion)).ToArray(),
                result.Elapsed.TotalMilliseconds, result.FilesAnalyzed,
                result.Failures.Select(f => new FailureResponse(f.RuleId, f.Message)).ToArray()),
            new StatisticsResponse(stats.DocumentsAnalyzed, stats.DocumentsReused, stats.CacheHits,
                stats.ProjectsAnalyzed, stats.ProjectsReused, stats.ProjectsInWorkspace, stats.CompilerDiagnostics,
                stats.SharpLintDiagnostics, stats.ReconciliationMs, stats.WorkspaceUpdateMs, stats.RuleExecutionMs,
                stats.AggregationMs, stats.TotalMs), snapshot.InputFingerprint, snapshot.Validated);
    }
}
