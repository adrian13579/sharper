using SharpLint.Core.Naming;

namespace SharpLint.Core.Analysis;

public sealed class AnalysisContext
{
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyList<NamingRule> NamingRules { get; init; }
}
