using Microsoft.CodeAnalysis;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;

namespace SharpLint.Core.Analysis;

public sealed class AnalysisContext
{
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyList<NamingRule> NamingRules { get; init; }
    public IReadOnlyList<ConfiguredRule> Rules { get; init; } = [];
    public IReadOnlyList<Compilation> Compilations { get; init; } = [];
    public IReadOnlyDictionary<Compilation, string> CompilationProjects { get; init; } = new Dictionary<Compilation, string>();
    public RuleRegistry Registry { get; init; } = RuleRegistry.Default;
    /// <summary>Null when this host cannot validate a compilation prerequisite.</summary>
    public bool? CompilationHasErrors { get; init; }
}
