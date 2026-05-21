using SharpLint.Core.Naming;

namespace SharpLint.Tests.Naming;

public sealed class ConventionMatcherTests
{
    [Theory]
    [InlineData("FooBar", true)]
    [InlineData("F", true)]
    [InlineData("fooBar", false)]
    [InlineData("foo_bar", false)]
    public void PascalCase(string name, bool expected) =>
        Assert.Equal(expected, ConventionMatcher.Matches(name, NamingConvention.PascalCase));

    [Theory]
    [InlineData("fooBar", true)]
    [InlineData("_fooBar", true)]
    [InlineData("FooBar", false)]
    [InlineData("foo_bar", false)]
    public void CamelCase(string name, bool expected) =>
        Assert.Equal(expected, ConventionMatcher.Matches(name, NamingConvention.CamelCase));

    [Theory]
    [InlineData("foo_bar", true)]
    [InlineData("foo", true)]
    [InlineData("FooBar", false)]
    [InlineData("FOO_BAR", false)]
    public void SnakeCase(string name, bool expected) =>
        Assert.Equal(expected, ConventionMatcher.Matches(name, NamingConvention.SnakeCase));

    [Theory]
    [InlineData("FOO_BAR", true)]
    [InlineData("FOO", true)]
    [InlineData("Foo_Bar", false)]
    [InlineData("foo_bar", false)]
    public void UpperSnakeCase(string name, bool expected) =>
        Assert.Equal(expected, ConventionMatcher.Matches(name, NamingConvention.UpperSnakeCase));

    [Fact]
    public void Namespace_checks_each_segment()
    {
        Assert.True(ConventionMatcher.MatchesNamespace("Acme.Foo.Bar", NamingConvention.PascalCase));
        Assert.False(ConventionMatcher.MatchesNamespace("acme.foo", NamingConvention.PascalCase));
    }
}
