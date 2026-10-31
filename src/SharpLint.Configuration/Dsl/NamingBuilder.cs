using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Selects which symbols a naming rule targets. Each selector returns a
/// <see cref="NamingRuleChain"/> on which you attach the actual requirement
/// (e.g. <c>n.Classes().RequireConvention(NamingConvention.PascalCase)</c>).
/// </summary>
public sealed class NamingBuilder
{
    private readonly List<NamingRule> _rules;

    internal NamingBuilder(List<NamingRule> rules) { _rules = rules; }

    /// <summary>Targets all methods, regardless of accessibility or modifiers.</summary>
    public NamingRuleChain Methods() => new(_rules, SymbolKind.Method, PredicateFlags.None);

    /// <summary>
    /// Targets methods, optionally narrowed by accessibility or <c>async</c>
    /// (e.g. <c>m =&gt; m.Public()</c>, <c>m =&gt; m.Async()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which methods the rule applies to.</param>
    public NamingRuleChain Methods(Action<MethodPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new MethodPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Method, pb.Flags);
    }

    /// <summary>Targets all fields, regardless of accessibility or modifiers.</summary>
    public NamingRuleChain Fields() => new(_rules, SymbolKind.Field, PredicateFlags.None);

    /// <summary>
    /// Targets fields, optionally narrowed by accessibility or <c>const</c>
    /// (e.g. <c>f =&gt; f.Private()</c>, <c>f =&gt; f.Const()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which fields the rule applies to.</param>
    public NamingRuleChain Fields(Action<FieldPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new FieldPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Field, pb.Flags);
    }

    /// <summary>Targets all class declarations, regardless of accessibility or modifiers.</summary>
    public NamingRuleChain Classes() => new(_rules, SymbolKind.Class, PredicateFlags.None);

    /// <summary>
    /// Targets classes, optionally narrowed by accessibility or a modifier such as
    /// <c>static</c>/<c>abstract</c>/<c>sealed</c> (e.g. <c>c =&gt; c.Abstract()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which classes the rule applies to.</param>
    public NamingRuleChain Classes(Action<ClassPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new ClassPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Class, pb.Flags);
    }

    /// <summary>Targets all interface declarations, regardless of accessibility.</summary>
    public NamingRuleChain Interfaces() => new(_rules, SymbolKind.Interface, PredicateFlags.None);

    /// <summary>
    /// Targets interfaces, optionally narrowed by accessibility
    /// (e.g. <c>i =&gt; i.Public()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which interfaces the rule applies to.</param>
    public NamingRuleChain Interfaces(Action<InterfacePredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new InterfacePredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Interface, pb.Flags);
    }

    /// <summary>Targets all record declarations, regardless of accessibility or modifiers.</summary>
    public NamingRuleChain Records() => new(_rules, SymbolKind.Record, PredicateFlags.None);

    /// <summary>
    /// Targets records, optionally narrowed by accessibility or a modifier such as
    /// <c>abstract</c>/<c>sealed</c> (e.g. <c>r =&gt; r.Sealed()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which records the rule applies to.</param>
    public NamingRuleChain Records(Action<RecordPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new RecordPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Record, pb.Flags);
    }

    /// <summary>Targets all enum declarations, regardless of accessibility.</summary>
    public NamingRuleChain Enums() => new(_rules, SymbolKind.Enum, PredicateFlags.None);

    /// <summary>
    /// Targets enums, optionally narrowed by accessibility
    /// (e.g. <c>e =&gt; e.Public()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which enums the rule applies to.</param>
    public NamingRuleChain Enums(Action<EnumPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new EnumPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Enum, pb.Flags);
    }

    /// <summary>Targets all properties, regardless of accessibility or modifiers.</summary>
    public NamingRuleChain Properties() => new(_rules, SymbolKind.Property, PredicateFlags.None);

    /// <summary>
    /// Targets properties, optionally narrowed by accessibility or <c>static</c>
    /// (e.g. <c>p =&gt; p.Public()</c>, <c>p =&gt; p.Static()</c>).
    /// </summary>
    /// <param name="predicates">Filters that restrict which properties the rule applies to.</param>
    public NamingRuleChain Properties(Action<PropertyPredicateBuilder> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var pb = new PropertyPredicateBuilder();
        predicates(pb);
        return new NamingRuleChain(_rules, SymbolKind.Property, pb.Flags);
    }

    /// <summary>Targets method and constructor parameters.</summary>
    public NamingRuleChain Parameters() => new(_rules, SymbolKind.Parameter, PredicateFlags.None);

    /// <summary>Targets local variables.</summary>
    public NamingRuleChain LocalVariables() => new(_rules, SymbolKind.LocalVariable, PredicateFlags.None);

    /// <summary>Targets namespace declarations.</summary>
    public NamingRuleChain Namespaces() => new(_rules, SymbolKind.Namespace, PredicateFlags.None);
}
