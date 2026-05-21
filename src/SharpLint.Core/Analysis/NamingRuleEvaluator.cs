using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;
using Diagnostic = SharpLint.Core.Diagnostics.Diagnostic;
using SymbolKind = SharpLint.Core.Naming.SymbolKind;

namespace SharpLint.Core.Analysis;

internal static class NamingRuleEvaluator
{
    public static bool Applies(NamingRule rule, SymbolInfo symbol)
    {
        if (rule.Kind != symbol.Kind) return false;
        if (rule.Predicates == PredicateFlags.None) return true;
        return (symbol.Predicates & rule.Predicates) == rule.Predicates;
    }

    public static bool IsExempt(SymbolInfo symbol, NamingRule rule)
    {
        if (rule.ExemptAttributes.Count == 0) return false;

        var attributeLists = GetAttributeLists(symbol.DeclaringNode);
        if (attributeLists is null) return false;

        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = attr.Name.ToString();
                foreach (var exempt in rule.ExemptAttributes)
                {
                    if (NameMatches(name, exempt)) return true;
                }
            }
        }

        return false;
    }

    public static bool IsViolated(NamingRule rule, string name)
    {
        var positive = rule.Direction == Direction.Require;
        var holds = EvaluateVerb(rule, name);
        return positive ? !holds : holds;
    }

    public static Diagnostic CreateDiagnostic(NamingRule rule, SymbolInfo symbol)
    {
        var lineSpan = symbol.Location.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        return new Diagnostic(
            RuleId: rule.RuleId,
            Severity: rule.Severity,
            Message: BuildMessage(rule, symbol.Name),
            Location: new SourceLocation(symbol.Location.SourceTree?.FilePath ?? string.Empty, start.Line + 1, start.Character + 1));
    }

    private static bool EvaluateVerb(NamingRule rule, string name) => rule.Verb switch
    {
        VerbKind.Convention => rule.Kind == SymbolKind.Namespace
            ? ConventionMatcher.MatchesNamespace(name, rule.Convention!.Value)
            : ConventionMatcher.Matches(name, rule.Convention!.Value),
        VerbKind.Prefix => rule.Values.Any(p => name.StartsWith(p, StringComparison.Ordinal)),
        VerbKind.Suffix => rule.Values.Any(s => name.EndsWith(s, StringComparison.Ordinal)),
        VerbKind.Name => rule.Values.Any(v => string.Equals(name, v, StringComparison.Ordinal)),
        VerbKind.Regex => Regex.IsMatch(name, Anchor(rule.Pattern!)),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Verb, null),
    };

    private static string Anchor(string pattern)
    {
        var anchored = pattern;
        if (!anchored.StartsWith('^')) anchored = "^" + anchored;
        if (!anchored.EndsWith('$')) anchored += "$";
        return anchored;
    }

    private static string BuildMessage(NamingRule rule, string name)
    {
        var subject = rule.Kind.ToString();
        var verb = rule.Direction == Direction.Require ? "must" : "must not";
        return rule.Verb switch
        {
            VerbKind.Convention => $"{subject} '{name}' {verb} use {rule.Convention} convention.",
            VerbKind.Prefix => $"{subject} '{name}' {verb} start with {Quote(rule.Values)}.",
            VerbKind.Suffix => $"{subject} '{name}' {verb} end with {Quote(rule.Values)}.",
            VerbKind.Name => $"{subject} '{name}' {verb} be named {Quote(rule.Values)}.",
            VerbKind.Regex => $"{subject} '{name}' {verb} match pattern '{rule.Pattern}'.",
            _ => $"{subject} '{name}' violates {rule.RuleId}.",
        };
    }

    private static string Quote(IReadOnlyList<string> values) =>
        values.Count == 1 ? $"'{values[0]}'" : "any of [" + string.Join(", ", values.Select(v => $"'{v}'")) + "]";

    private static SyntaxList<AttributeListSyntax>? GetAttributeLists(SyntaxNode node) => node switch
    {
        MemberDeclarationSyntax m => m.AttributeLists,
        ParameterSyntax p => p.AttributeLists,
        LocalDeclarationStatementSyntax l => l.AttributeLists,
        _ => null,
    };

    private static bool NameMatches(string usedName, string fullAttributeName)
    {
        var simple = fullAttributeName;
        var lastDot = simple.LastIndexOf('.');
        if (lastDot >= 0) simple = simple[(lastDot + 1)..];
        if (simple.EndsWith("Attribute", StringComparison.Ordinal))
            simple = simple[..^"Attribute".Length];

        var used = usedName;
        var usedDot = used.LastIndexOf('.');
        if (usedDot >= 0) used = used[(usedDot + 1)..];
        if (used.EndsWith("Attribute", StringComparison.Ordinal))
            used = used[..^"Attribute".Length];

        return string.Equals(used, simple, StringComparison.Ordinal);
    }
}
