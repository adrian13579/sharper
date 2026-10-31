using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;
using CoreRules = SharpLint.Core.Rules.Rules;

namespace SharpLint.Tests.Analysis;

public sealed class TaxonomyAnalyzerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharplint-taxonomy-{Guid.NewGuid():N}");
    public TaxonomyAnalyzerTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    public static IEnumerable<object[]> Categories() => CoreRules.All.Select(c => new object[] { c.Id });

    private AnalysisContext Context(SharpLintConfig config, RuleRegistry? registry = null) => new()
    {
        WorkingDirectory = _directory,
        NamingRules = config.NamingRules,
        Rules = config.Rules ?? [],
        Registry = registry ?? RuleRegistry.Default,
    };

    private static RuleRegistry Registry(string category, IRuleAnalyzer? analyzer = null) => new([
        new("example", category == "extension" ? [new("extension", "Extension")] : [],
            [new(new("example.check", category, RuleScope.Type), analyzer ?? new TestPolicyAnalyzer())]),
    ]);

    [Theory]
    [MemberData(nameof(Categories))]
    [InlineData("extension")]
    public async Task Registered_policies_analyze_compliant_and_violating_source_in_every_category(string category)
    {
        var path = Path.Combine(_directory, "Sample.cs");
        File.WriteAllText(path, "// heading\nclass Allowed {}\nclass Forbidden {}");
        var config = Lint.Configure(c => c.Rule("example.check", JsonSerializer.SerializeToElement(new { forbiddenName = "Forbidden" }), Severity.Error));
        // The wire format carries options and severity, without attempting to serialize analyzers.
        config = JsonSerializer.Deserialize<SharpLintConfig>(JsonSerializer.Serialize(config))!;
        var analyzer = new Analyzer();
        var context = Context(config, Registry(category));
        var result = await analyzer.AnalyzeAsync(context);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("example.check", diagnostic.RuleId);
        Assert.Equal(category, diagnostic.CategoryId);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal("Test policy violation.", diagnostic.Message);
        Assert.Equal(new SourceLocation(path, 3, 7), diagnostic.Location);
        Assert.Equal("Choose another name.", diagnostic.Suggestion);
        Assert.Equal(1, result.FilesAnalyzed);
        Assert.True(result.HasErrors);
        Assert.Equal(result.Diagnostics, (await analyzer.AnalyzeAsync(context)).Diagnostics);
        Assert.Equal("// heading\nclass Allowed {}\nclass Forbidden {}", File.ReadAllText(path));
        File.WriteAllText(path, "class Allowed {}");
        Assert.Empty((await analyzer.AnalyzeAsync(context)).Diagnostics);
    }

    [Fact]
    public async Task Empty_categories_and_unconfigured_registrations_emit_nothing()
    {
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), "class Forbidden {}");
        var config = Lint.Configure(_ => { });
        Assert.Empty((await new Analyzer().AnalyzeAsync(Context(config))).Diagnostics);
        var throwing = Registry("syntax", new ThrowingAnalyzer());
        Assert.Empty((await new Analyzer().AnalyzeAsync(Context(config, throwing))).Diagnostics);
        var hidden = Lint.Configure(c => c.Rule("example.check", JsonSerializer.SerializeToElement(new { }), Severity.Hidden));
        Assert.Empty((await new Analyzer().AnalyzeAsync(Context(hidden, throwing))).Diagnostics);
    }

    [Fact]
    public async Task Missing_analyzers_and_duplicate_configurations_fail_even_without_source_files()
    {
        var options = JsonSerializer.SerializeToElement(new { forbiddenName = "Forbidden" });
        var rule = new ConfiguredRule("missing", options, Severity.Hidden);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new Analyzer().AnalyzeAsync(Context(new([]) { Rules = [rule] })));
        Assert.Contains("missing", error.Message);
        var duplicate = new ConfiguredRule("example.check", options);
        var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => new Analyzer().AnalyzeAsync(Context(
            new([]) { Rules = [duplicate, duplicate] }, Registry("syntax"))));
        Assert.Contains("Duplicate configured rule", repeated.Message);
    }

    [Fact]
    public async Task Naming_and_module_rules_keep_metadata_and_deterministic_source_order()
    {
        File.WriteAllText(Path.Combine(_directory, "Z.cs"), "class bad {} class Forbidden {}");
        File.WriteAllText(Path.Combine(_directory, "A.cs"), "class bad {} class Forbidden {}");
        var config = Lint.Configure(c => c
            .Naming(n => n.Classes().RequireConvention(NamingConvention.PascalCase))
            .Rule("example.check", JsonSerializer.SerializeToElement(new { forbiddenName = "Forbidden" })));
        var analyzer = new Analyzer();
        var context = Context(config, Registry("design"));
        var result = await analyzer.AnalyzeAsync(context);
        Assert.Equal(new[] { "A.cs", "A.cs", "Z.cs", "Z.cs" }, result.Diagnostics.Select(d => Path.GetFileName(d.Location.FilePath)));
        Assert.Equal(new[] { "naming", "design", "naming", "design" }, result.Diagnostics.Select(d => d.CategoryId));
        Assert.Equal(new[] { "naming.class.convention", "example.check", "naming.class.convention", "example.check" }, result.Diagnostics.Select(d => d.RuleId));
        Assert.All(result.Diagnostics, d => Assert.Equal(Severity.Warning, d.Severity));
        Assert.Equal(result.Diagnostics, (await analyzer.AnalyzeAsync(context)).Diagnostics);
    }

    [Fact]
    public async Task Final_order_uses_file_then_line_then_column_and_preserves_ties()
    {
        var config = Lint.Configure(c => c.Rule("example.check", JsonSerializer.SerializeToElement(new { })));
        var result = await new Analyzer().AnalyzeAsync(Context(config, Registry("project", new UnorderedAnalyzer())));
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, result.Diagnostics.Select(d => d.Message));
        Assert.All(result.Diagnostics, d => Assert.Null(d.Suggestion));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Analyzer_receives_host_directory_options_and_cancellation(bool returnsViolation)
    {
        using var cancellation = new CancellationTokenSource();
        var config = Lint.Configure(c => c.Rule("example.check", JsonSerializer.SerializeToElement(new { })));
        var callback = new CancelingAnalyzer(_directory, cancellation, returnsViolation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Analyzer().AnalyzeAsync(
            Context(config, Registry("reliability", callback)), cancellation.Token));
        Assert.True(callback.Called);
    }

    [Fact]
    public async Task Pre_canceled_analysis_stops_before_accessing_the_file_system()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Analyzer().AnalyzeAsync(new AnalysisContext
        {
            WorkingDirectory = Path.Combine(_directory, "does-not-exist"), NamingRules = [],
        }, cancellation.Token));
    }

    // Test policies demonstrate the shared extension path, not shipped rules for the new categories.
    private sealed class TestPolicyAnalyzer : IRuleAnalyzer
    {
        public async ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken)
        {
            var violations = new List<RuleViolation>();
            Assert.Equal(context.SyntaxTrees.Select(t => t.FilePath).OrderBy(p => p, StringComparer.Ordinal),
                context.SyntaxTrees.Select(t => t.FilePath));
            foreach (var tree in context.SyntaxTrees.Reverse())
            {
                var root = await tree.GetRootAsync(cancellationToken);
                foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (declaration.Identifier.ValueText != options.GetProperty("forbiddenName").GetString()) continue;
                    var start = declaration.Identifier.GetLocation().GetLineSpan().StartLinePosition;
                    violations.Add(new("Test policy violation.", new(tree.FilePath, start.Line + 1, start.Character + 1), "Choose another name."));
                }
            }
            return violations;
        }
    }

    private sealed class ThrowingAnalyzer : IRuleAnalyzer
    {
        public ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unconfigured or disabled analyzer must not run.");
    }

    private sealed class UnorderedAnalyzer : IRuleAnalyzer
    {
        public ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RuleViolation>>([
                new("e", new("Z.cs", 1, 1)), new("c", new("A.cs", 2, 3)),
                new("b", new("A.cs", 2, 1)), new("a", new("A.cs", 1, 9)), new("d", new("A.cs", 2, 3)),
            ]);
    }

    private sealed class CancelingAnalyzer(string directory, CancellationTokenSource cancellation, bool returnsViolation) : IRuleAnalyzer
    {
        public bool Called { get; private set; }
        public ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken)
        {
            Called = true;
            Assert.Equal(directory, context.WorkingDirectory);
            Assert.Equal(cancellation.Token, cancellationToken);
            Assert.Equal(JsonValueKind.Object, options.ValueKind);
            cancellation.Cancel();
            return ValueTask.FromResult<IReadOnlyList<RuleViolation>>(
                returnsViolation ? [new("Canceled", new("A.cs", 1, 1))] : []);
        }
    }
}
