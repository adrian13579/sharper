using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpLint.Analysis;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;
using CoreRules = SharpLint.Core.Rules.Rules;

// These analyzer fixtures live in the test assembly and are loaded explicitly, not shipped to the compiler.
#pragma warning disable RS1001, RS1036, RS1038, RS1041, RS2008

namespace SharpLint.Tests.Analysis;

public sealed class RoslynModuleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharplint-roslyn-{Guid.NewGuid():N}");
    public RoslynModuleTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private static RuleModule Module() => new("acme.security", "Acme security", [new ExternalAnalyzer()]);

    [Fact]
    public async Task External_module_roundtrips_and_runs_semantic_Roslyn_callbacks_in_the_workspace()
    {
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), "class Allowed {} class Forbidden : Allowed {}");
        var config = Lint.Configure(c => c.UseModule(Module(), m => m.Rule("ACME001", new { forbiddenBase = "Allowed" }, Severity.Error)));
        var json = JsonSerializer.Serialize(config);
        Assert.Contains("Acme security", json);
        Assert.DoesNotContain("SupportedDiagnostics", json);
        var loaded = JsonSerializer.Deserialize<SharpLintConfig>(json)!;
        var registry = loaded.CreateRegistry();
        Assert.Equal("acme.security", registry.GetRuleModuleId("ACME001"));
        Assert.DoesNotContain(registry.Categories, c => c.Id == "Acme concern");
        using var workspace = new WorkspaceCoordinator(_directory, _ => Task.FromResult(loaded), watch: false);
        var first = await workspace.CheckAsync();
        var diagnostic = Assert.Single(first.Result.Diagnostics, d => d.RuleId == "ACME001");
        Assert.Equal("Acme concern", diagnostic.CategoryId);
        Assert.Equal("acme.security", diagnostic.ModuleId);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal("Forbidden inherits Allowed", diagnostic.Message);
        Assert.Equal("https://example.com/ACME001", diagnostic.HelpUrl);
        Assert.True(diagnostic.HasSourceLocation);
        Assert.NotNull(diagnostic.SpanStart);
        Assert.NotNull(diagnostic.SpanLength);
        Assert.DoesNotContain(first.Result.Diagnostics, d => d.RuleId == "ACME002");
        Assert.Equal(first.Result.Diagnostics, (await workspace.CheckAsync()).Result.Diagnostics);
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), "class Allowed {} class Forbidden {}");
        Assert.DoesNotContain((await workspace.CheckAsync()).Result.Diagnostics, d => d.RuleId == "ACME001");
    }

    [Fact]
    public async Task Source_suppression_and_hidden_configuration_are_honored()
    {
        var tree = CSharpSyntaxTree.ParseText("#pragma warning disable ACME001\nclass Allowed {} class Forbidden : Allowed {}");
        var compilation = CSharpCompilation.Create("Test", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        foreach (var severity in new[] { Severity.Warning, Severity.Hidden })
        {
            var config = Lint.Configure(c => c.UseModule(Module(), m => m.Rule("ACME001", new { forbiddenBase = "Allowed" }, severity)));
            var result = await new Analyzer().AnalyzeTreesAsync(new SharpLint.Core.Analysis.AnalysisContext
            {
                WorkingDirectory = _directory, NamingRules = [], Rules = config.Rules!,
                Registry = config.CreateRegistry(), Compilations = [compilation],
            }, [tree]);
            Assert.Empty(result.Diagnostics);
        }
    }

    [Fact]
    public async Task Analyzer_failures_are_explicit_and_compilation_findings_have_no_fake_source_location()
    {
        var registry = new RuleRegistry([new("external", "External", [new CompilationAnalyzer()])]);
        var config = Lint.Configure(c => c.Rule("GLOBAL001", JsonSerializer.SerializeToElement(new { })));
        var context = new SharpLint.Core.Analysis.AnalysisContext { WorkingDirectory = _directory, NamingRules = [], Rules = config.Rules!, Registry = registry };
        var diagnostic = Assert.Single((await new Analyzer().AnalyzeAsync(context)).Diagnostics);
        Assert.False(diagnostic.HasSourceLocation);
        Assert.Null(diagnostic.SpanStart);
        config = Lint.Configure(c => c.Rule("GLOBAL001", JsonSerializer.SerializeToElement(new { fail = true })));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new Analyzer().AnalyzeAsync(new SharpLint.Core.Analysis.AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = config.Rules!, Registry = registry,
        }));
        Assert.Contains("GLOBAL001", error.Message);
        Assert.Equal("Provider failure", error.InnerException!.Message);
    }

    [Fact]
    public async Task Roslyn_findings_keep_project_identity_for_shared_source_and_compilation_callbacks()
    {
        var tree = CSharpSyntaxTree.ParseText("class Allowed {} class Forbidden : Allowed {}", path: "Shared.cs");
        var references = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var first = CSharpCompilation.Create("First", [tree], references);
        var second = CSharpCompilation.Create("Second", [tree], references);
        var config = Lint.Configure(c => c.UseModule(Module(), m => m.Rule("ACME001", new { forbiddenBase = "Allowed" })));
        var result = await new Analyzer().AnalyzeTreesAsync(new SharpLint.Core.Analysis.AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = config.Rules!, Registry = config.CreateRegistry(),
            Compilations = [first, second], CompilationProjects = new Dictionary<Compilation, string> { [first] = "First.csproj", [second] = "Second.csproj" },
        }, [tree]);
        Assert.Equal(new[] { "First.csproj", "Second.csproj" }, result.Diagnostics.Select(d => d.Project));
        Assert.All(result.Diagnostics, d => Assert.Equal("ACME001", d.RuleId));
    }

    [Fact]
    public void Modules_do_not_require_categories_and_descriptor_ID_conflicts_are_rejected()
    {
        var module = Module();
        var registry = new RuleRegistry([module]);
        Assert.Equal("Acme concern", registry.GetRule("ACME001").Definition.CategoryId);
        Assert.Equal(9, registry.Categories.Count);
        Assert.Throws<ArgumentException>(() => new RuleRegistry([module, new("other", "Other", [new ExternalAnalyzer()])]));
        Assert.Throws<ArgumentException>(() => new RuleRegistry([new("syntax", "Impostor", [new ExternalAnalyzer()])]));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(module).UseModule(module)));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.Module(module, _ => { })));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(CoreRules.Design)));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.Module(CoreRules.Syntax, m => m.Rule("ACME001"))));
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.UseModule(new("private", "Private", [new PrivateAnalyzer()]))));
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ExternalAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new("ACME001", "Base policy", "{0} inherits {1}", "Acme concern", DiagnosticSeverity.Warning,
            isEnabledByDefault: false, helpLinkUri: "https://example.com/ACME001");
        private static readonly DiagnosticDescriptor Other = new("ACME002", "Other", "Other diagnostic", "Unrelated", DiagnosticSeverity.Warning, true);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule, Other];
        public override void Initialize(Microsoft.CodeAnalysis.Diagnostics.AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(c =>
            {
                var type = (INamedTypeSymbol)c.Symbol;
                if (c.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("sharplint.ACME001.forbiddenBase", out var forbidden)
                    && type.BaseType?.Name == forbidden)
                    c.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(Rule, type.Locations[0], type.Name, forbidden));
                c.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(Other, type.Locations[0]));
            }, Microsoft.CodeAnalysis.SymbolKind.NamedType);
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CompilationAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new("GLOBAL001", "Global", "Global finding", "Provider category", DiagnosticSeverity.Warning, true, customTags: WellKnownDiagnosticTags.CompilationEnd);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];
        public override void Initialize(Microsoft.CodeAnalysis.Diagnostics.AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(c =>
            {
                if (c.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("sharplint.GLOBAL001.fail", out var fail) && fail == "true")
                    throw new InvalidOperationException("Provider failure");
                c.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(Rule, Location.None));
            });
        }
    }

    private sealed class PrivateAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [];
        public override void Initialize(Microsoft.CodeAnalysis.Diagnostics.AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
        }
    }
}
