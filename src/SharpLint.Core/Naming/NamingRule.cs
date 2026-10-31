using SharpLint.Core.Diagnostics;

namespace SharpLint.Core.Naming;

public sealed record NamingRule
{
    /// <summary>Identifies a composed selector chain. Null treats this rule as a standalone declaration.</summary>
    public int? ChainId { get; init; }
    public SymbolKind Kind { get; init; }
    public PredicateFlags Predicates { get; init; }
    public VerbKind Verb { get; init; }
    public Direction Direction { get; init; }
    public NamingConvention? Convention { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
    public string? Pattern { get; init; }
    public Severity Severity { get; init; } = Severity.Warning;
    public IReadOnlyList<string> ExemptAttributes { get; init; } = [];

    public string RuleId => RuleIdFormatter.Format(Kind, Predicates, Verb);
}
