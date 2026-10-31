using SharpLint.Core.Naming;

namespace SharpLint.Tests.Naming;

public sealed class NamingContractTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData(".Acme", false)]
    [InlineData("Acme.", false)]
    [InlineData("Acme..Tools", false)]
    [InlineData("Acme.Tools", true)]
    public void Namespace_rejects_empty_segments(string name, bool expected) =>
        Assert.Equal(expected, ConventionMatcher.MatchesNamespace(name, NamingConvention.PascalCase));

    [Theory]
    [InlineData(SymbolKind.Parameter, "parameter")]
    [InlineData(SymbolKind.LocalVariable, "local")]
    public void Rule_ids_use_stable_kind_tokens(SymbolKind kind, string token) =>
        Assert.Equal($"naming.{token}.name", RuleIdFormatter.Format(kind, PredicateFlags.None, VerbKind.Name));

    [Fact]
    public void Rule_ids_order_all_predicates_before_the_verb() =>
        Assert.Equal("naming.method.public.internal.protected.private.async.const.static.abstract.sealed.regex",
            RuleIdFormatter.Format(SymbolKind.Method, PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected |
                PredicateFlags.Private | PredicateFlags.Async | PredicateFlags.Const | PredicateFlags.Static | PredicateFlags.Abstract | PredicateFlags.Sealed, VerbKind.Regex));

    [Fact]
    public void Unknown_enum_values_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConventionMatcher.Matches("Name", (NamingConvention)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuleIdFormatter.Format((SymbolKind)99, PredicateFlags.None, VerbKind.Name));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuleIdFormatter.Format(SymbolKind.Class, PredicateFlags.None, (VerbKind)99));
    }
}
