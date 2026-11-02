using System.Text.Json;
using SharpLint.Configuration;
using SharpLint.Configuration.Dsl;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Tests.Configuration;

public sealed class FluentPolicyConfigurationTests
{
    internal static void Configure(ConfigBuilder config, string id, JsonElement options)
    {
        string[] Values(string key) => options.GetProperty(key).EnumerateArray().Select(value => value.GetString()!).ToArray();
        int Maximum(int fallback) => options.TryGetProperty("maximum", out var value) ? value.GetInt32() : fallback;
        switch (id)
        {
            case "syntax.no-goto": config.Syntax(s => s.GotoStatements().Forbid().AsError()); break;
            case "syntax.file-scoped-namespaces": config.Syntax(s => s.Namespaces().RequireFileScoped().AsError()); break;
            case "organization.one-type-per-file": config.Organization(o => o.Files().RequireOneType().AsError()); break;
            case "organization.match-type-file-name": config.Organization(o => o.Files().RequireMatchingTypeName().AsError()); break;
            case "design.maximum-parameters": config.Design(d => d.Parameters().RequireMaximumCount(Maximum(5)).AsError()); break;
            case "design.maximum-type-members": config.Design(d => d.TypeMembers().RequireMaximumCount(Maximum(20)).AsError()); break;
            case "dependencies.forbidden-usings": config.Dependencies(d => d.Usings().ForbidNamespaces(Values("namespaces")).AsError()); break;
            case "dependencies.forbidden-packages": config.Dependencies(d => d.Packages().ForbidNames(Values("packages")).AsError()); break;
            case "architecture.namespace-boundaries": config.Architecture(a => a.Namespaces(options.GetProperty("from").GetString()!).ForbidReferencesTo(Values("to")).AsError()); break;
            case "architecture.forbidden-inheritance": config.Architecture(a => a.Inheritance().ForbidTypes(Values("types")).AsError()); break;
            case "reliability.empty-catch": config.Reliability(r => r.CatchBlocks().ForbidEmpty().AsError()); break;
            case "reliability.rethrow-caught-exception": config.Reliability(r => r.Rethrows().RequirePreservedStackTrace().AsError()); break;
            case "performance.empty-array-allocation": config.Performance(p => p.Arrays().ForbidEmptyAllocation().AsError()); break;
            case "performance.explicit-boxing": config.Performance(p => p.Conversions().ForbidExplicitBoxing().AsError()); break;
            case "project.nullable-enabled": config.Project(p => p.Nullable().RequireEnabled().AsError()); break;
            case "project.target-frameworks": config.Project(p => p.TargetFrameworks().RequireAllowed(Values("allowed")).AsError()); break;
            default: throw new ArgumentException($"No fluent test configuration for '{id}'.", nameof(id));
        }
    }

    [Fact]
    public void Selectors_alone_enable_nothing_and_category_callbacks_return_the_configuration()
    {
        var config = Lint.Configure(c =>
        {
            Assert.Same(c, c.Syntax(s => { s.GotoStatements(); s.Namespaces(); }));
            Assert.Same(c, c.Organization(o => o.Files()));
            Assert.Same(c, c.Design(d => { d.Parameters(); d.TypeMembers(); }));
            Assert.Same(c, c.Dependencies(d => { d.Usings(); d.Packages(); }));
            Assert.Same(c, c.Architecture(a => { a.Namespaces("Domain"); a.Inheritance(); }));
            Assert.Same(c, c.Reliability(r => { r.CatchBlocks(); r.Rethrows(); }));
            Assert.Same(c, c.Performance(p => { p.Arrays(); p.Conversions(); }));
            Assert.Same(c, c.Project(p => { p.Nullable(); p.TargetFrameworks(); }));
        });
        Assert.Null(config.Rules);
        Assert.Null(config.Modules);
    }

    [Fact]
    public void Modifiers_apply_to_the_latest_requirement_on_their_own_chain_and_preserve_fluent_type()
    {
        var config = Lint.Configure(c => c.Organization(o =>
        {
            var first = o.Files();
            Assert.Same(first, first.RequireOneType());
            var second = o.Files().RequireMatchingTypeName();
            Assert.Same(first, first.AsError());
            Assert.Same(second, second.Disabled());
            Assert.Same(first, first.AsInfo());
            Assert.Same(first, first.AsWarning());
        }));
        Assert.Equal(new[] { "organization.one-type-per-file", "organization.match-type-file-name" }, config.Rules!.Select(r => r.RuleId));
        Assert.Equal(new[] { Severity.Warning, Severity.Hidden }, config.Rules!.Select(r => r.Severity));

        var chained = Lint.Configure(c => c.Organization(o => o.Files()
            .RequireOneType().AsError()
            .RequireMatchingTypeName().AsInfo()));
        Assert.Equal(new[] { Severity.Error, Severity.Info }, chained.Rules!.Select(r => r.Severity));
    }

    [Fact]
    public void Defaults_and_snapshot_survive_builder_reuse_and_json_roundtrip()
    {
        ParameterDesignRuleChain? captured = null;
        var config = Lint.Configure(c => c.Design(d =>
        {
            captured = d.Parameters().RequireMaximumCount();
            d.TypeMembers().RequireMaximumCount();
        }));
        captured!.AsError();
        var roundtrip = JsonSerializer.Deserialize<SharpLintConfig>(JsonSerializer.Serialize(config))!;
        Assert.Equal(new[] { 5, 20 }, roundtrip.Rules!.Select(r => r.Options.GetProperty("maximum").GetInt32()));
        Assert.All(roundtrip.Rules!, rule => Assert.Equal(Severity.Warning, rule.Severity));
        Assert.Empty(roundtrip.NamingRules);
        Assert.Null(roundtrip.Modules);
    }

    [Fact]
    public void Options_are_snapshotted_and_each_requirement_preserves_its_own_values()
    {
        var namespaces = new[] { "System.IO", "System.Net" };
        var config = Lint.Configure(c => c
            .Dependencies(d => d.Usings().ForbidNamespaces(namespaces))
            .Architecture(a => a.Namespaces("Domain").ForbidReferencesTo("Infrastructure", "Persistence")));
        namespaces[0] = "Changed";
        Assert.Equal(new[] { "System.IO", "System.Net" }, config.Rules![0].Options.GetProperty("namespaces").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("Domain", config.Rules![1].Options.GetProperty("from").GetString());
        Assert.Equal(new[] { "Infrastructure", "Persistence" }, config.Rules![1].Options.GetProperty("to").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public void Duplicate_ids_are_rejected_across_fluent_and_generic_configuration()
    {
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c
            .Syntax(s => s.GotoStatements().Forbid())
            .Syntax(s => s.GotoStatements().Forbid())));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c
            .Syntax(s => s.GotoStatements().Forbid())
            .Rule("syntax.no-goto", JsonSerializer.SerializeToElement(new { }))));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c
            .Rule("syntax.no-goto", JsonSerializer.SerializeToElement(new { }))
            .Syntax(s => s.GotoStatements().Forbid())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Count_limits_must_be_positive(int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lint.Configure(c => c.Design(d => d.Parameters().RequireMaximumCount(maximum))));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lint.Configure(c => c.Design(d => d.TypeMembers().RequireMaximumCount(maximum))));
    }

    public static IEnumerable<object?[]> InvalidValues()
    {
        yield return [null];
        yield return [Array.Empty<string>()];
        yield return [new[] { "" }];
        yield return [new[] { " " }];
        yield return [new[] { "Valid", null! }];
        yield return [new[] { "Valid", "" }];
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void Lists_reject_missing_or_blank_values_at_configuration_time(string[]? values)
    {
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Dependencies(d => d.Usings().ForbidNamespaces(values!))));
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Dependencies(d => d.Packages().ForbidNames(values!))));
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Architecture(a => a.Namespaces("Domain").ForbidReferencesTo(values!))));
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Architecture(a => a.Inheritance().ForbidTypes(values!))));
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Project(p => p.TargetFrameworks().RequireAllowed(values!))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Source_namespace_cannot_be_blank(string? from) =>
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Architecture(a => a.Namespaces(from!))));

    [Fact]
    public void Null_category_callbacks_are_rejected()
    {
        var builder = new ConfigBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Syntax(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Organization(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Design(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Dependencies(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Architecture(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Reliability(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Performance(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Project(null!));
    }

    [Fact]
    public void Modifiers_require_a_preceding_requirement()
    {
        Lint.Configure(c => c.Organization(o =>
        {
            var chain = o.Files();
            Assert.Throws<InvalidOperationException>(() => chain.AsError());
            Assert.Throws<InvalidOperationException>(() => chain.AsWarning());
            Assert.Throws<InvalidOperationException>(() => chain.AsInfo());
            Assert.Throws<InvalidOperationException>(() => chain.Disabled());
        }));
    }
}
