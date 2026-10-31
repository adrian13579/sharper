using System.Text.Json.Serialization;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;
using RuleCategories = SharpLint.Core.Rules.Rules;

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

    [JsonIgnore]
    public string ModuleId => RuleCategories.CoreModuleId;

    [JsonIgnore]
    public RuleDefinition Definition => new(RuleId, RuleCategories.Naming.Id, Kind switch
    {
        SymbolKind.Namespace => RuleScope.Namespace,
        SymbolKind.Class or SymbolKind.Interface or SymbolKind.Record or SymbolKind.Enum => RuleScope.Type,
        _ => RuleScope.Symbol,
    });
}
