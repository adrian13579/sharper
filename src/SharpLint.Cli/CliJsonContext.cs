using System.Text.Json.Serialization;
using SharpLint.Daemon;

namespace SharpLint.Cli;

internal sealed record CheckOutput(string Status, IReadOnlyList<FailureResponse> Failures,
    int FilesAnalyzed, double ElapsedMs, IReadOnlyList<DiagnosticResponse> Diagnostics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CliStatistics? Statistics);

internal sealed record CliStatistics(long SnapshotVersion, bool Validated, string InputFingerprint,
    bool DaemonConnected, [property: JsonIgnore] StatisticsResponse Values, double ConnectionMs, double CliElapsedMs)
{
    public int DocumentsAnalyzed => Values.DocumentsAnalyzed;
    public int DocumentsReused => Values.DocumentsReused;
    public int CacheHits => Values.CacheHits;
    public int ProjectsAnalyzed => Values.ProjectsAnalyzed;
    public int ProjectsReused => Values.ProjectsReused;
    public int ProjectsInWorkspace => Values.ProjectsInWorkspace;
    public int CompilerDiagnostics => Values.CompilerDiagnostics;
    public int SharpLintDiagnostics => Values.SharpLintDiagnostics;
    public double ReconciliationMs => Values.ReconciliationMs;
    public double WorkspaceUpdateMs => Values.WorkspaceUpdateMs;
    public double RuleExecutionMs => Values.RuleExecutionMs;
    public double AggregationMs => Values.AggregationMs;
    public double TotalMs => Values.TotalMs;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CheckOutput))]
[JsonSerializable(typeof(CliStatistics))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
