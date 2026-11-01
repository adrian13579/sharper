using CoreRules = SharpLint.Core.Rules.Rules;
using System.Text.Json;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;

namespace SharpLint.Tests.Rules;

public sealed class RuleRegistryTests
{
    public static IEnumerable<object[]> Categories()
    {
        yield return [CoreRules.Naming, "naming", "Naming"];
        yield return [CoreRules.Syntax, "syntax", "Syntax"];
        yield return [CoreRules.Organization, "organization", "Organization"];
        yield return [CoreRules.Design, "design", "Design"];
        yield return [CoreRules.Dependencies, "dependencies", "Dependencies"];
        yield return [CoreRules.Architecture, "architecture", "Architecture"];
        yield return [CoreRules.Reliability, "reliability", "Reliability"];
        yield return [CoreRules.Performance, "performance", "Performance"];
        yield return [CoreRules.Project, "project", "Project"];
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void Built_in_modules_have_stable_ids_and_own_their_rules(RuleModule category, string id, string name)
    {
        Assert.Equal(id, category.Id);
        Assert.Equal(name, category.DisplayName);
        Assert.Contains(category, CoreRules.All);
        Assert.Contains(category, RuleRegistry.Default.Modules);
        Assert.Equal(id, RuleRegistry.Default.GetCategoryModuleId(id));
    }

    [Fact]
    public void Core_catalog_is_complete_unique_and_read_only()
    {
        Assert.Equal(9, CoreRules.All.Count);
        Assert.Equal(9, RuleRegistry.Default.Categories.Count);
        Assert.Equal(9, CoreRules.All.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<NotSupportedException>(() => ((IList<RuleModule>)CoreRules.All).Clear());
        Assert.Equal(new[] { "architecture", "dependencies", "design", "naming", "organization", "performance", "project", "reliability", "syntax" },
            RuleRegistry.Default.Categories.Select(c => c.Id));
    }

    [Theory]
    [InlineData(null, "Display")]
    [InlineData("", "Display")]
    [InlineData(" ", "Display")]
    [InlineData("id", null)]
    [InlineData("id", "")]
    [InlineData("id", " ")]
    public void Categories_require_identifiers_and_display_names(string? id, string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => new RuleCategory(id!, name!));

    [Fact]
    public void Categories_are_identified_by_id_rather_than_display_name()
    {
        var registry = new RuleRegistry([new("custom", [new("first", "Same"), new("second", "Same")], [])]);
        Assert.Equal(11, registry.Categories.Count);
        Assert.Equal("custom", registry.GetCategoryModuleId("first"));
        Assert.Equal("custom", registry.GetCategoryModuleId("second"));
    }

    [Theory]
    [InlineData(SymbolKind.Class, RuleScope.Type)]
    [InlineData(SymbolKind.Interface, RuleScope.Type)]
    [InlineData(SymbolKind.Record, RuleScope.Type)]
    [InlineData(SymbolKind.Enum, RuleScope.Type)]
    [InlineData(SymbolKind.Namespace, RuleScope.Namespace)]
    [InlineData(SymbolKind.Method, RuleScope.Symbol)]
    [InlineData(SymbolKind.Property, RuleScope.Symbol)]
    [InlineData(SymbolKind.Field, RuleScope.Symbol)]
    [InlineData(SymbolKind.Parameter, RuleScope.Symbol)]
    [InlineData(SymbolKind.LocalVariable, RuleScope.Symbol)]
    public void Naming_metadata_preserves_identity_and_classifies_scope(SymbolKind kind, RuleScope scope)
    {
        var rule = new NamingRule { Kind = kind, Verb = VerbKind.Prefix };
        Assert.Equal(new RuleDefinition(rule.RuleId, "naming", scope), rule.Definition);
        Assert.Equal("naming", rule.ModuleId);
        var json = JsonSerializer.Serialize(rule);
        Assert.DoesNotContain("Definition", json);
        Assert.DoesNotContain("ModuleId", json);
    }

    private static RuleRegistration Registration(string id = "custom.rule", string category = "design", RuleScope scope = RuleScope.SourceFile) =>
        new(new(id, category, scope), new EmptyAnalyzer());

    [Fact]
    public void Modules_can_share_core_categories_use_extension_categories_and_vary_scope()
    {
        var first = Registration();
        var second = Registration("other.rule", "custom-category", RuleScope.Repository);
        var modules = new RuleModule[]
        {
            new("z-provider", [new("custom-category", "Custom")], [first]),
            new("a-provider", [], [second]),
        };
        var registry = new RuleRegistry(modules);
        var reversed = new RuleRegistry(modules.Reverse());
        Assert.Same(first, registry.GetRule("custom.rule"));
        Assert.Same(second, registry.GetRule("other.rule"));
        Assert.Equal("z-provider", registry.GetRuleModuleId("custom.rule"));
        Assert.Equal("a-provider", registry.GetRuleModuleId("other.rule"));
        Assert.Equal("z-provider", registry.GetCategoryModuleId("custom-category"));
        Assert.Equal(RuleScope.SourceFile, registry.GetRule("custom.rule").Definition.Scope);
        Assert.Equal(RuleScope.Repository, registry.GetRule("other.rule").Definition.Scope);
        Assert.Equal(registry.Categories, reversed.Categories);
        Assert.Equal(first, reversed.GetRule("custom.rule"));
    }

    [Theory]
    [InlineData("module")]
    [InlineData("category")]
    [InlineData("rule")]
    public void Conflicts_are_rejected_in_either_registration_order(string kind)
    {
        RuleModule[] modules = kind switch
        {
            "module" => [new("same", [], []), new("same", [], [])],
            "category" => [new("a", [new("custom", "One")], []), new("b", [new("custom", "Two")], [])],
            _ => [new("a", [], [Registration()]), new("b", [], [Registration()])],
        };
        var error = Assert.Throws<ArgumentException>(() => new RuleRegistry(modules));
        var reversed = Assert.Throws<ArgumentException>(() => new RuleRegistry(modules.Reverse()));
        Assert.Equal(error.Message, reversed.Message);
        Assert.Contains(kind, error.Message);
        if (kind == "module") Assert.Contains("Duplicate module identifier 'same'.", error.Message);
    }

    [Fact]
    public void Multiple_invalid_registrations_report_the_first_identifier_in_ordinal_order()
    {
        var categoryError = Assert.Throws<ArgumentException>(() => new RuleRegistry([
            new("module", [new("syntax", "Syntax"), new("architecture", "Architecture")], []),
        ]));
        Assert.Contains("'architecture'", categoryError.Message);
        var ruleError = Assert.Throws<ArgumentException>(() => new RuleRegistry([
            new("module", [], [Registration("z", "missing-z"), Registration("a", "missing-a")]),
        ]));
        Assert.Contains("'missing-a'", ruleError.Message);
        var moduleError = Assert.Throws<ArgumentException>(() => new RuleRegistry([
            new("z", [], [Registration("z", "missing-z")]), new("a", [], [Registration("a", "missing-a")]),
        ]));
        Assert.Contains("'missing-a'", moduleError.Message);
    }

    [Fact]
    public void Reserved_core_registrations_and_unknown_categories_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new("sharplint", [], [])]));
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new("custom", [new("naming", "Renamed")], [])]));
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new("custom", [], [Registration("naming.class.prefix")])]));
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new("custom", [], [Registration(category: "missing")])]));
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new(" ", [], [])]));
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new("custom", [], [Registration("")])]));
        Assert.Throws<ArgumentNullException>(() => new RuleRegistry([new("custom", [], [new(new("id", "design", RuleScope.Type), null!)])]));
        Assert.Contains("no registered analyzer", Assert.Throws<InvalidOperationException>(() => RuleRegistry.Default.GetRule("missing")).Message);
    }

    [Fact]
    public void Registry_snapshots_registrations_and_identifiers_are_ordinal()
    {
        var categories = new List<RuleCategory> { new("Custom", "Custom") };
        var rules = new List<RuleRegistration> { Registration("R", "Custom"), Registration("r") };
        var registry = new RuleRegistry([new("module", categories, rules)]);
        categories.Clear();
        rules.Clear();
        Assert.Equal("Custom", registry.GetRule("R").Definition.CategoryId);
        Assert.Equal("design", registry.GetRule("r").Definition.CategoryId);
        Assert.Equal("module", registry.GetCategoryModuleId("Custom"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetCategoryModuleId("custom"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetRuleModuleId("missing"));
    }

    [Fact]
    public void Naming_reservation_applies_to_the_dotted_prefix_only()
    {
        var registry = new RuleRegistry([new("custom", [], [Registration("naming-adjacent.rule")])]);
        Assert.Equal("naming-adjacent.rule", registry.GetRule("naming-adjacent.rule").Definition.Id);
    }

    private sealed class EmptyAnalyzer : IRuleAnalyzer
    {
        public ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RuleViolation>>([]);
    }
}
