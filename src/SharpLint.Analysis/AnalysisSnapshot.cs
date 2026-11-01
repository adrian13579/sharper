using Microsoft.CodeAnalysis;
using SharpLint.Core.Analysis;

namespace SharpLint.Analysis;

public sealed record WorkspaceSnapshot(long Version, Solution Solution,
    string ConfigurationFingerprint, string ProjectGraphFingerprint, string InputFingerprint);

public sealed record CheckStatistics(int DocumentsAnalyzed, int DocumentsReused, int CacheHits,
    double ReconciliationMs, double WorkspaceUpdateMs, double RuleExecutionMs, double AggregationMs,
    double TotalMs)
{
    public int ProjectsAnalyzed { get; init; }
    public int ProjectsReused { get; init; }
    public int ProjectsInWorkspace => ProjectsAnalyzed + ProjectsReused;
    public int CompilerDiagnostics { get; init; }
    public int SharpLintDiagnostics { get; init; }
}

/// <summary>Validated means equal content observations before and after analysis, not an OS atomic snapshot.</summary>
public sealed record SnapshotResult(long SnapshotVersion, AnalysisResult Result, CheckStatistics Statistics,
    string InputFingerprint, bool Validated = true);
