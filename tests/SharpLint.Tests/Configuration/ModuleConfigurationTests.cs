using System.Text.Json;
using SharpLint.Configuration;
using SharpLint.Core.Rules;
using SharpLint.Tests.Analysis;
using CoreRules = SharpLint.Core.Rules.Rules;

namespace SharpLint.Tests.Configuration;

public sealed class ModuleConfigurationTests
{
    [Fact]
    public void Roslyn_modules_expose_their_owned_rules_without_category_registration()
    {
        var analyzers = new List<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer> { new RoslynModuleTests.ExternalAnalyzer() };
        var module = new RuleModule("example", "Example", analyzers);
        analyzers.Clear();
        Assert.Single(module.Analyzers);
        Assert.Empty(module.Categories);
        Assert.Equal(new[] { "ACME001", "ACME002" }, module.Rules.Select(r => r.Definition.Id));
        Assert.Equal(new[] { "Acme concern", "Unrelated" }, module.Rules.Select(r => r.Definition.CategoryId));
        Assert.Throws<NotSupportedException>(() => ((IList<RuleRegistration>)module.Rules).Clear());
    }

    [Fact]
    public void Registry_combines_configuration_modules_with_host_and_builtins()
    {
        var host = new RuleRegistry([new("host", "Host", [new RoslynModuleTests.CompilationAnalyzer()])]);
        var config = Lint.Configure(c => c.UseModule(new("example", "Example", [new RoslynModuleTests.ExternalAnalyzer()])));
        var registry = config.CreateRegistry(host);
        Assert.Equal(11, registry.Modules.Count);
        Assert.Equal("host", registry.GetRuleModuleId("GLOBAL001"));
        Assert.Equal("example", registry.GetRuleModuleId("ACME001"));
        Assert.Equal("syntax", registry.GetRuleModuleId("syntax.no-goto"));
        Assert.Equal(10, ConfigLoader.Default().CreateRegistry(host).Modules.Count);
    }

    [Fact]
    public void Module_configuration_snapshots_builder_state_and_checks_ownership()
    {
        ConfigBuilder? captured = null;
        var module = new RuleModule("example", "Example", [new RoslynModuleTests.ExternalAnalyzer()]);
        var config = Lint.Configure(c =>
        {
            captured = c;
            Assert.Same(c, c.UseModule(module, m => Assert.Same(m, m.Rule("ACME001"))));
            Assert.Same(c, c.Module(CoreRules.Syntax, m => m.Rule("syntax.no-goto")));
        });
        captured!.UseModule(new("other", "Other", [new RoslynModuleTests.CompilationAnalyzer()]));
        Assert.Single(config.Modules!);
        Assert.Equal(new[] { "ACME001", "syntax.no-goto" }, config.Rules!.Select(r => r.RuleId));
        Assert.Equal(JsonValueKind.Object, config.Rules![0].Options.ValueKind);
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(module, m => m.Rule("missing"))));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(new("syntax", "Impostor", [new RoslynModuleTests.ExternalAnalyzer()]))));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(new("sharplint", "Impostor", [new RoslynModuleTests.ExternalAnalyzer()]))));
        Assert.Throws<ArgumentNullException>(() => Lint.Configure(c => c.UseModule(null!)));
        Assert.Throws<ArgumentNullException>(() => Lint.Configure(c => c.Module(null!, _ => { })));
        Assert.Throws<ArgumentNullException>(() => Lint.Configure(c => c.Module(CoreRules.Syntax, null!)));
    }

    [Fact]
    public void Invalid_serialized_analyzer_references_fail_explicitly()
    {
        static SharpLintConfig Config(string path, string type) => new([])
        {
            Modules = [new("external", "External", [new(path, type)])],
        };
        var assembly = typeof(RoslynModuleTests.ExternalAnalyzer).Assembly.Location;
        Assert.Contains("absolute", Assert.Throws<InvalidOperationException>(() => Config("relative.dll", "Type").CreateRegistry()).Message);
        Assert.Throws<TypeLoadException>(() => Config(assembly, "Nonexistent.Type").CreateRegistry());
        Assert.Contains("DiagnosticAnalyzer", Assert.Throws<InvalidOperationException>(() => Config(assembly, typeof(NotAnAnalyzer).FullName!).CreateRegistry()).Message);
    }

    [Fact]
    public void Invalid_module_configuration_identifies_the_problem_and_the_selected_identifiers()
    {
        Assert.Contains("Roslyn DiagnosticAnalyzer", Assert.Throws<ArgumentException>(() =>
            Lint.Configure(c => c.UseModule(new("empty", "Empty", [])))).Message);
        var module = new RuleModule("example", "Example", [new RoslynModuleTests.ExternalAnalyzer()]);
        Assert.Contains("example", Assert.Throws<ArgumentException>(() =>
            Lint.Configure(c => c.UseModule(module).UseModule(module))).Message);
        Assert.Contains("UseModule", Assert.Throws<ArgumentException>(() =>
            Lint.Configure(c => c.Module(module, _ => { }))).Message);
        var error = Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(module, m => m.Rule("missing.rule"))));
        Assert.Contains("example", error.Message);
        Assert.Contains("missing.rule", error.Message);
        Assert.Contains("duplicate.rule", Assert.Throws<ArgumentException>(() =>
            Lint.Configure(c => c.Rule("duplicate.rule", JsonSerializer.SerializeToElement(new { }))
                .Rule("duplicate.rule", JsonSerializer.SerializeToElement(new { })))).Message);
    }

    [Fact]
    public void Empty_configuration_omits_optional_rule_and_module_fields_on_the_wire()
    {
        var config = Lint.Configure(_ => { });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("rules", out _));
        Assert.False(json.RootElement.TryGetProperty("modules", out _));
        Assert.Empty(json.RootElement.GetProperty("namingRules").EnumerateArray());
    }

    [Fact]
    public void Multiple_analyzers_in_one_assembly_share_the_same_loaded_provider_types()
    {
        var config = Lint.Configure(c => c.UseModule(new("example", "Example",
            [new RoslynModuleTests.ExternalAnalyzer(), new RoslynModuleTests.CompilationAnalyzer()])));
        var analyzers = config.CreateRegistry().Modules.Single(m => m.Id == "example").Analyzers;
        Assert.Equal(2, analyzers.Count);
        Assert.Same(analyzers[0].GetType().Assembly, analyzers[1].GetType().Assembly);
    }

    public sealed class NotAnAnalyzer;
}
