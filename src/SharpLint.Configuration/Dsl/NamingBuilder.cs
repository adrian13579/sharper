using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

public sealed class NamingBuilder
{
    private readonly List<NamingRule> _rules;

    internal NamingBuilder(List<NamingRule> rules) { _rules = rules; }

    public NamingRuleChain Methods(Action<MethodPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new MethodPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Method, pb.Flags);
    }

    public NamingRuleChain Fields(Action<FieldPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new FieldPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Field, pb.Flags);
    }

    public NamingRuleChain Classes() => new(_rules, SymbolKind.Class, PredicateFlags.None);
    public NamingRuleChain Interfaces() => new(_rules, SymbolKind.Interface, PredicateFlags.None);
    public NamingRuleChain Records() => new(_rules, SymbolKind.Record, PredicateFlags.None);
    public NamingRuleChain Enums() => new(_rules, SymbolKind.Enum, PredicateFlags.None);
    public NamingRuleChain Properties() => new(_rules, SymbolKind.Property, PredicateFlags.None);
    public NamingRuleChain Parameters() => new(_rules, SymbolKind.Parameter, PredicateFlags.None);
    public NamingRuleChain LocalVariables() => new(_rules, SymbolKind.LocalVariable, PredicateFlags.None);
    public NamingRuleChain Namespaces() => new(_rules, SymbolKind.Namespace, PredicateFlags.None);
}
