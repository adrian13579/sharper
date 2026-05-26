using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// A naming rule under construction for a selected symbol kind. Attach a verb
/// (<c>Require…</c>/<c>Forbid…</c>) to state the requirement, then optionally chain a
/// severity (<see cref="AsError"/>, <see cref="AsWarning"/>, …) or an exemption.
/// Each verb call adds a new rule; modifiers apply to the most recent one.
/// </summary>
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

    /// <summary>Requires names to match the given casing convention.</summary>
    /// <param name="convention">The casing the name must follow.</param>
    public NamingRuleChain RequireConvention(NamingConvention convention) =>
        Add(Direction.Require, VerbKind.Convention, convention: convention);

    /// <summary>Forbids names that match the given casing convention.</summary>
    /// <param name="convention">The casing the name must not follow.</param>
    public NamingRuleChain ForbidConvention(NamingConvention convention) =>
        Add(Direction.Forbid, VerbKind.Convention, convention: convention);

    /// <summary>Requires names to start with one of the given prefixes (e.g. <c>"I"</c> for interfaces).</summary>
    /// <param name="prefixes">Allowed prefixes; a name satisfies the rule by starting with any one.</param>
    public NamingRuleChain RequirePrefix(params string[] prefixes) =>
        Add(Direction.Require, VerbKind.Prefix, values: prefixes);

    /// <summary>Forbids names that start with any of the given prefixes.</summary>
    /// <param name="prefixes">Disallowed prefixes.</param>
    public NamingRuleChain ForbidPrefix(params string[] prefixes) =>
        Add(Direction.Forbid, VerbKind.Prefix, values: prefixes);

    /// <summary>Requires names to end with one of the given suffixes (e.g. <c>"Async"</c> for async methods).</summary>
    /// <param name="suffixes">Allowed suffixes; a name satisfies the rule by ending with any one.</param>
    public NamingRuleChain RequireSuffix(params string[] suffixes) =>
        Add(Direction.Require, VerbKind.Suffix, values: suffixes);

    /// <summary>Forbids names that end with any of the given suffixes.</summary>
    /// <param name="suffixes">Disallowed suffixes.</param>
    public NamingRuleChain ForbidSuffix(params string[] suffixes) =>
        Add(Direction.Forbid, VerbKind.Suffix, values: suffixes);

    /// <summary>Requires the name to be exactly one of the given values.</summary>
    /// <param name="names">The only names allowed.</param>
    public NamingRuleChain RequireName(params string[] names) =>
        Add(Direction.Require, VerbKind.Name, values: names);

    /// <summary>Forbids the given exact names (e.g. reserved or discouraged identifiers).</summary>
    /// <param name="names">Names that are not allowed.</param>
    public NamingRuleChain ForbidName(params string[] names) =>
        Add(Direction.Forbid, VerbKind.Name, values: names);

    /// <summary>Requires names to match the given regular expression.</summary>
    /// <param name="pattern">A .NET regular expression the name must match.</param>
    public NamingRuleChain RequireRegex(string pattern) =>
        Add(Direction.Require, VerbKind.Regex, pattern: pattern);

    /// <summary>Forbids names that match the given regular expression.</summary>
    /// <param name="pattern">A .NET regular expression the name must not match.</param>
    public NamingRuleChain ForbidRegex(string pattern) =>
        Add(Direction.Forbid, VerbKind.Regex, pattern: pattern);

    /// <summary>Reports violations of the most recent rule as errors (fails the build/check).</summary>
    public NamingRuleChain AsError() => SetSeverity(Severity.Error);

    /// <summary>Reports violations of the most recent rule as warnings. This is the default.</summary>
    public NamingRuleChain AsWarning() => SetSeverity(Severity.Warning);

    /// <summary>Reports violations of the most recent rule as informational messages.</summary>
    public NamingRuleChain AsInfo() => SetSeverity(Severity.Info);

    /// <summary>Keeps the most recent rule defined but silenced, so it produces no diagnostics.</summary>
    public NamingRuleChain Disabled() => SetSeverity(Severity.Hidden);

    /// <summary>
    /// Exempts symbols carrying the given attribute from the most recent rule
    /// (e.g. <c>ExceptWithAttribute&lt;ObsoleteAttribute&gt;()</c>).
    /// </summary>
    /// <typeparam name="T">The attribute type that marks a symbol as exempt.</typeparam>
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
