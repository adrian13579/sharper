using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpLint.Analysis;

/// <summary>Positions are zero-based UTF-16 in every output format. Paths are absolute.</summary>
public sealed record SharpLintDiagnostic
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? StartColumn { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
    public string? Source { get; init; }
    public string? HelpUrl { get; init; }
    public IReadOnlyList<RelatedDiagnostic>? RelatedInformation { get; init; }
    public JsonElement? Data { get; init; }
    public IReadOnlyList<int>? Tags { get; init; }
}

public sealed record RelatedDiagnostic(string Message, string? FilePath, int? StartLine, int? StartColumn,
    int? EndLine, int? EndColumn);

public sealed record DiagnosticResponse(string Status, string Scope, string RequestedPath, bool Complete,
    string Freshness, IReadOnlyList<SharpLintDiagnostic> Diagnostics, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Limitations, double DurationMs)
{
    public int DiagnosticCount => Diagnostics.Count;
}

public sealed record ServiceStatus(bool Available, string WorkspaceState, string Workspace,
    string? Solution, bool DiagnosticsAvailable, string? LastError, int SessionStarts,
    int? ProcessId, string? ServerName, IReadOnlyList<string> DiagnosticProviders,
    bool WorkspaceDiagnosticCapability, IReadOnlyList<string> Limitations);

public static class DiagnosticJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
