using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

public sealed class NamingRuleChain
{
    private readonly List<NamingRule> _rules;
    private readonly SymbolKind _kind;
    private readonly PredicateFlags _predicates;
    private int _lastIndex = -1;

    internal NamingRuleChain(List<NamingRule> rules, SymbolKind kind, PredicateFlags predicates)
    {
        _rules = rules;
        _kind = kind;
        _predicates = predicates;
    }

    public NamingRuleChain RequireConvention(NamingConvention convention) =>
        Add(Direction.Require, VerbKind.Convention, convention: convention);

    public NamingRuleChain ForbidConvention(NamingConvention convention) =>
        Add(Direction.Forbid, VerbKind.Convention, convention: convention);

    public NamingRuleChain RequirePrefix(params string[] prefixes) =>
        Add(Direction.Require, VerbKind.Prefix, values: prefixes);

    public NamingRuleChain ForbidPrefix(params string[] prefixes) =>
        Add(Direction.Forbid, VerbKind.Prefix, values: prefixes);

    public NamingRuleChain RequireSuffix(params string[] suffixes) =>
        Add(Direction.Require, VerbKind.Suffix, values: suffixes);

    public NamingRuleChain ForbidSuffix(params string[] suffixes) =>
        Add(Direction.Forbid, VerbKind.Suffix, values: suffixes);

    public NamingRuleChain RequireName(params string[] names) =>
        Add(Direction.Require, VerbKind.Name, values: names);

    public NamingRuleChain ForbidName(params string[] names) =>
        Add(Direction.Forbid, VerbKind.Name, values: names);

    public NamingRuleChain RequireRegex(string pattern) =>
        Add(Direction.Require, VerbKind.Regex, pattern: pattern);

    public NamingRuleChain ForbidRegex(string pattern) =>
        Add(Direction.Forbid, VerbKind.Regex, pattern: pattern);

    public NamingRuleChain AsError() => SetSeverity(Severity.Error);
    public NamingRuleChain AsWarning() => SetSeverity(Severity.Warning);
    public NamingRuleChain AsInfo() => SetSeverity(Severity.Info);
    public NamingRuleChain Disabled() => SetSeverity(Severity.Hidden);

    public NamingRuleChain ExceptWithAttribute<T>() where T : Attribute
    {
        EnsureRule();
        var rule = _rules[_lastIndex];
        var name = typeof(T).FullName ?? typeof(T).Name;
        _rules[_lastIndex] = rule with { ExemptAttributes = [.. rule.ExemptAttributes, name] };
        return this;
    }

    private NamingRuleChain Add(
        Direction direction,
        VerbKind verb,
        NamingConvention? convention = null,
        string[]? values = null,
        string? pattern = null)
    {
        var rule = new NamingRule
        {
            Kind = _kind,
            Predicates = _predicates,
            Verb = verb,
            Direction = direction,
            Convention = convention,
            Values = values ?? [],
            Pattern = pattern,
        };
        _lastIndex = _rules.Count;
        _rules.Add(rule);
        return this;
    }

    private NamingRuleChain SetSeverity(Severity severity)
    {
        EnsureRule();
        _rules[_lastIndex] = _rules[_lastIndex] with { Severity = severity };
        return this;
    }

    private void EnsureRule()
    {
        if (_lastIndex < 0)
            throw new InvalidOperationException("A modifier was called before any Require…/Forbid… verb on this selector.");
    }
}
