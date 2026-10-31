using RuleCategories = SharpLint.Core.Rules.Rules;

namespace SharpLint.Core.Naming;

public static class RuleIdFormatter
{
    public static string Format(SymbolKind kind, PredicateFlags predicates, VerbKind verb)
    {
        var parts = new List<string>(4) { RuleCategories.Naming.Id, KindToken(kind) };
        parts.AddRange(PredicateTokens(predicates));
        parts.Add(VerbToken(verb));
        return string.Join('.', parts);
    }

    private static string KindToken(SymbolKind kind) => kind switch
    {
        SymbolKind.Method => "method",
        SymbolKind.Class => "class",
        SymbolKind.Interface => "interface",
        SymbolKind.Record => "record",
        SymbolKind.Enum => "enum",
        SymbolKind.Property => "property",
        SymbolKind.Field => "field",
        SymbolKind.Parameter => "parameter",
        SymbolKind.LocalVariable => "local",
        SymbolKind.Namespace => "namespace",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static IEnumerable<string> PredicateTokens(PredicateFlags predicates)
    {
        if (predicates.HasFlag(PredicateFlags.Public)) yield return "public";
        if (predicates.HasFlag(PredicateFlags.Internal)) yield return "internal";
        if (predicates.HasFlag(PredicateFlags.Protected)) yield return "protected";
        if (predicates.HasFlag(PredicateFlags.Private)) yield return "private";
        if (predicates.HasFlag(PredicateFlags.Async)) yield return "async";
        if (predicates.HasFlag(PredicateFlags.Const)) yield return "const";
        if (predicates.HasFlag(PredicateFlags.Static)) yield return "static";
        if (predicates.HasFlag(PredicateFlags.Abstract)) yield return "abstract";
        if (predicates.HasFlag(PredicateFlags.Sealed)) yield return "sealed";
    }

    private static string VerbToken(VerbKind verb) => verb switch
    {
        VerbKind.Convention => "convention",
        VerbKind.Prefix => "prefix",
        VerbKind.Suffix => "suffix",
        VerbKind.Name => "name",
        VerbKind.Regex => "regex",
        _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
    };
}
