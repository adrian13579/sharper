namespace SharpLint.Analysis;

public sealed record DiagnosticServiceOptions
{
    public string RootPath { get; init; } = Directory.GetCurrentDirectory();
    public string? SolutionPath { get; init; }
    public string? ServerPath { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}
