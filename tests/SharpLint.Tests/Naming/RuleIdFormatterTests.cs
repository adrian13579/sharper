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
}
