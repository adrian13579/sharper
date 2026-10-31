using SharpLint.Core.Naming;

namespace SharpLint.Tests.Naming;

public sealed class RuleIdFormatterTests
{
    [Fact]
    public void Method_public_convention()
    {
        Assert.Equal(
            "naming.method.public.convention",
            RuleIdFormatter.Format(SymbolKind.Method, PredicateFlags.Public, VerbKind.Convention));
    }

    [Fact]
    public void Method_async_suffix()
    {
        Assert.Equal(
            "naming.method.async.suffix",
            RuleIdFormatter.Format(SymbolKind.Method, PredicateFlags.Async, VerbKind.Suffix));
    }

    [Fact]
    public void Class_convention_has_no_predicate_segment()
    {
        Assert.Equal(
            "naming.class.convention",
            RuleIdFormatter.Format(SymbolKind.Class, PredicateFlags.None, VerbKind.Convention));
    }

    [Fact]
    public void Field_private_prefix()
    {
        Assert.Equal(
            "naming.field.private.prefix",
            RuleIdFormatter.Format(SymbolKind.Field, PredicateFlags.Private, VerbKind.Prefix));
    }

    [Fact]
    public void Field_const_convention()
    {
        Assert.Equal(
            "naming.field.const.convention",
            RuleIdFormatter.Format(SymbolKind.Field, PredicateFlags.Const, VerbKind.Convention));
    }

    [Fact]
    public void Namespace_prefix()
    {
        Assert.Equal(
            "naming.namespace.prefix",
            RuleIdFormatter.Format(SymbolKind.Namespace, PredicateFlags.None, VerbKind.Prefix));
    }

    [Fact]
    public void Class_abstract_prefix()
    {
        Assert.Equal(
            "naming.class.abstract.prefix",
            RuleIdFormatter.Format(SymbolKind.Class, PredicateFlags.Abstract, VerbKind.Prefix));
    }

    [Fact]
    public void Record_sealed_convention()
    {
        Assert.Equal(
            "naming.record.sealed.convention",
            RuleIdFormatter.Format(SymbolKind.Record, PredicateFlags.Sealed, VerbKind.Convention));
    }

    [Fact]
    public void Property_static_convention()
    {
        Assert.Equal(
            "naming.property.static.convention",
            RuleIdFormatter.Format(SymbolKind.Property, PredicateFlags.Static, VerbKind.Convention));
    }

    [Fact]
    public void Combined_predicates_render_accessibility_first()
    {
        Assert.Equal(
            "naming.class.public.abstract.suffix",
            RuleIdFormatter.Format(SymbolKind.Class, PredicateFlags.Public | PredicateFlags.Abstract, VerbKind.Suffix));
    }
}
