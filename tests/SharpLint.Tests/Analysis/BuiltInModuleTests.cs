using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;
using SharpLint.Tests.Configuration;
using CoreRules = SharpLint.Core.Rules.Rules;

namespace SharpLint.Tests.Analysis;

public sealed class BuiltInModuleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharplint-builtins-{Guid.NewGuid():N}");
    private static readonly MetadataReference[] References = [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

    public BuiltInModuleTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    // Serializable data lets xUnit and mutation runners select individual policies rather than rerunning every row.
    public static IEnumerable<object[]> SourcePolicies() => SourceCases().SelectMany(row => new[] { false, true }.Select(fluent =>
        new object[] { row[0], JsonSerializer.Serialize(row[1]), row[2], row[3], fluent }));

    private static IEnumerable<object[]> SourceCases()
    {
        yield return ["syntax.no-goto", new { }, "class Sample { void M() { goto end; end: return; } }", "class Sample { void M() { return; } }"];
        yield return ["syntax.file-scoped-namespaces", new { }, "namespace Example { class Sample {} }", "namespace Example; class Sample {}"];
        yield return ["organization.one-type-per-file", new { }, "class Sample {} record Other;", "class Sample { class Nested {} }"];
        yield return ["organization.match-type-file-name", new { }, "record Wrong;", "record Sample;"];
        yield return ["design.maximum-parameters", new { maximum = 2 }, "class Sample(int a, int b, int c);", "class Sample(int a, int b);"];
        yield return ["design.maximum-type-members", new { maximum = 1 }, "class Sample { int a; int b; }", "class Sample { int a; }"];
        yield return ["dependencies.forbidden-usings", new { namespaces = new[] { "System.IO" } }, "using Files = global::System.IO.File; class Sample {}", "namespace System.IOExtra { class C {} } namespace Example { using System.IOExtra; class Sample {} }"];
        yield return ["architecture.namespace-boundaries", new { from = "Domain", to = new[] { "Infrastructure" } }, "namespace Infrastructure { public class Service {} } namespace Domain { class Sample { Infrastructure.Service M() => new(); } }", "namespace Infrastructure { public class Service {} } namespace DomainExtra { class Sample { Infrastructure.Service M() => new(); } }"];
        yield return ["architecture.forbidden-inheritance", new { types = new[] { "System.Exception" } }, "class Sample : System.Exception {}", "class Sample {}"];
        yield return ["reliability.empty-catch", new { }, "class Sample { void M() { try {} catch { /* ignored */ } } }", "class Sample { void M() { try {} catch { throw; } } }"];
        yield return ["reliability.rethrow-caught-exception", new { }, "class Sample { void M() { try {} catch (System.Exception ex) { throw ex; } } }", "class Sample { void M() { try {} catch (System.Exception ex) { throw; } } }"];
        yield return ["performance.empty-array-allocation", new { }, "class Sample { int[] M() => new int[0]; }", "class Sample { int[] M() => System.Array.Empty<int>(); }"];
        yield return ["syntax.no-goto", new { }, "class Sample { void M(int x) { switch (x) { case 0: goto default; default: return; } } }", "class Sample { void M(int x) { switch (x) { case 0: break; default: return; } } }"];
        yield return ["organization.one-type-per-file", new { }, "interface Sample {} delegate void Other();", "interface Sample { delegate void Nested(); }"];
        yield return ["organization.match-type-file-name", new { }, "enum sample {}", "enum Sample {}"];
        yield return ["organization.match-type-file-name", new { }, "delegate void Other();", "delegate void Sample();"];
        yield return ["design.maximum-parameters", new { }, "class Sample { void M(int a,int b,int c,int d,int e,int f) {} }", "class Sample { void M(int a,int b,int c,int d,int e) {} }"];
        yield return ["design.maximum-type-members", new { maximum = 2 }, "class Sample { int a; int b; int c; }", "class Sample { int a; class Inner { int a,b,c; } }"];
        yield return ["dependencies.forbidden-usings", new { namespaces = new[] { "System.IO" } }, "global using System.IO; class Sample {}", "using System; class Sample {}"];
        yield return ["architecture.namespace-boundaries", new { from = "Domain", to = new[] { "Infrastructure" } }, "using Alias = Infrastructure.Service; namespace Infrastructure { public class Service {} } namespace Domain.Child { class Sample { Alias M() => new(); } }", "namespace InfrastructureExtra { public class Service {} } namespace Domain { class Sample { InfrastructureExtra.Service M() => new(); } }"];
        yield return ["architecture.forbidden-inheritance", new { types = new[] { "System.IDisposable" } }, "interface Sample : System.IDisposable {}", "interface Sample : System.IComparable {}"];
        yield return ["performance.empty-array-allocation", new { }, "class Sample { int[][] M() => new int[0][]; }", "class Sample { int[][] M() => System.Array.Empty<int[]>(); }"];
        yield return ["performance.empty-array-allocation", new { }, "class Sample { int[] M() => new int[0L]; }", "class Sample { int[] M() => new int[1L]; }"];
        yield return ["performance.empty-array-allocation", new { }, "class Sample { int[] M() => new int[0U]; }", "class Sample { int[] M() => new int[1U]; }"];
        yield return ["performance.empty-array-allocation", new { }, "class Sample { int[] M() => new int[0UL]; }", "class Sample { int[] M() => new int[1UL]; }"];
        yield return ["performance.explicit-boxing", new { }, "class Sample { object M() => (object)42; }", "class Sample { object M() => (object)\"text\"; }"];
    }

    [Theory]
    [MemberData(nameof(SourcePolicies))]
    public async Task Built_in_policies_execute_through_configuration_and_analysis(string id, string options, string bad, string good, bool fluent)
    {
        var module = CoreRules.All.Single(m => m.Rules.Any(r => r.Definition.Id == id));
        var config = Lint.Configure(c =>
        {
            var values = JsonSerializer.Deserialize<JsonElement>(options);
            if (fluent) FluentPolicyConfigurationTests.Configure(c, id, values);
            else c.Module(module, m => m.Rule(id, values, Severity.Error));
        });
        config = JsonSerializer.Deserialize<SharpLintConfig>(JsonSerializer.Serialize(config))!;
        var path = Path.Combine(_directory, "Sample.cs");
        File.WriteAllText(path, bad);
        var first = await Analyze(config, bad, path);
        Assert.True(first.IsComplete);
        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal(id, diagnostic.RuleId);
        Assert.Equal(module.Id, diagnostic.ModuleId);
        Assert.Equal(module.Id, diagnostic.CategoryId);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.Equal(path, diagnostic.Location.FilePath);
        Assert.True(diagnostic.Location.Line >= 1);
        Assert.True(diagnostic.Location.Column >= 1);
        Assert.Equal(first.Diagnostics, (await Analyze(config, bad, path)).Diagnostics);
        Assert.Equal(bad, File.ReadAllText(path));
        File.WriteAllText(path, good);
        Assert.Empty((await Analyze(config, good, path)).Diagnostics);
    }

    private Task<AnalysisResult> Analyze(SharpLintConfig config, string source, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var compilation = CSharpCompilation.Create("Test", [tree], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        return new Analyzer().AnalyzeTreesAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = config.Rules!, Registry = config.CreateRegistry(),
            Compilations = [compilation], CompilationHasErrors = false,
        }, [tree]);
    }

    [Fact]
    public async Task Fluent_disabled_requirements_do_not_hide_other_requirements_in_the_same_chain()
    {
        var config = Lint.Configure(c => c
            .Syntax(s => s.GotoStatements().Forbid().Disabled())
            .Organization(o => o.Files()
                .RequireOneType().AsError()
                .RequireMatchingTypeName().Disabled()));
        var result = await Analyze(config,
            "class Sample { void M() { goto end; end: return; } } class Other {}",
            Path.Combine(_directory, "Sample.cs"));
        Assert.True(result.IsComplete);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("organization.one-type-per-file", diagnostic.RuleId);
        Assert.Equal(Severity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("dependencies.forbidden-packages", "{\"packages\":[\"Unsafe.Package\"]}", "<ItemGroup><PackageReference Include=\"unsafe.package\" Version=\"1.0\" /></ItemGroup>", "<ItemGroup><PackageReference Include=\"Safe.Package\" Version=\"1.0\" /></ItemGroup>", false)]
    [InlineData("dependencies.forbidden-packages", "{\"packages\":[\"Unsafe.Package\"]}", "<ItemGroup><PackageReference Include=\"unsafe.package\" Version=\"1.0\" /></ItemGroup>", "<ItemGroup><PackageReference Include=\"Safe.Package\" Version=\"1.0\" /></ItemGroup>", true)]
    [InlineData("project.nullable-enabled", "{}", "<PropertyGroup><Nullable>disable</Nullable></PropertyGroup>", "<PropertyGroup><Nullable>enable</Nullable></PropertyGroup>", false)]
    [InlineData("project.nullable-enabled", "{}", "<PropertyGroup><Nullable>disable</Nullable></PropertyGroup>", "<PropertyGroup><Nullable>enable</Nullable></PropertyGroup>", true)]
    [InlineData("project.target-frameworks", "{\"allowed\":[\"net10.0\"]}", "<PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks></PropertyGroup>", "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>", false)]
    [InlineData("project.target-frameworks", "{\"allowed\":[\"net10.0\"]}", "<PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks></PropertyGroup>", "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>", true)]
    public async Task Project_policies_check_literal_declarations_without_source_files(string id, string options, string bad, string good, bool fluent)
    {
        using var document = JsonDocument.Parse(options);
        var config = Lint.Configure(c =>
        {
            if (fluent) FluentPolicyConfigurationTests.Configure(c, id, document.RootElement);
            else c.Rule(id, document.RootElement);
        });
        var path = Path.Combine(_directory, "Sample.csproj");
        File.WriteAllText(path, $"<Project>\n{bad}\n</Project>");
        var context = new AnalysisContext { WorkingDirectory = _directory, NamingRules = [], Rules = config.Rules! };
        var result = await new Analyzer().AnalyzeAsync(context);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(id, diagnostic.RuleId);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.Equal(id.Split('.')[0], diagnostic.ModuleId);
        Assert.Equal(path, diagnostic.Location.FilePath);
        Assert.Equal(2, diagnostic.Location.Line);
        Assert.Equal(0, result.FilesAnalyzed);
        File.WriteAllText(path, $"<Project>{good}</Project>");
        Assert.Empty((await new Analyzer().AnalyzeAsync(context)).Diagnostics);
    }

    [Theory]
    [InlineData("project.nullable-enabled", "{}")]
    [InlineData("project.target-frameworks", "{\"allowed\":[\"net10.0\"]}")]
    public async Task Project_policies_report_missing_declarations_and_skip_build_artifacts(string id, string options)
    {
        using var document = JsonDocument.Parse(options);
        File.WriteAllText(Path.Combine(_directory, "Sample.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(_directory, "obj"));
        File.WriteAllText(Path.Combine(_directory, "obj", "Ignored.csproj"), "<Project />");
        var result = await new Analyzer().AnalyzeAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = [new(id, document.RootElement)],
        });
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(result.Diagnostics).Message));
    }

    [Theory]
    [InlineData("design.maximum-parameters", "{\"maximum\":0}")]
    [InlineData("design.maximum-parameters", "{\"maximum\":1.5}")]
    [InlineData("design.maximum-parameters", "{\"maximum\":2147483648}")]
    [InlineData("dependencies.forbidden-usings", "{}")]
    [InlineData("dependencies.forbidden-usings", "{\"namespaces\":\"System\"}")]
    [InlineData("dependencies.forbidden-usings", "{\"namespaces\":[1]}")]
    [InlineData("design.maximum-type-members", "{\"maximum\":\"2\"}")]
    [InlineData("dependencies.forbidden-usings", "{\"namespaces\":[]}")]
    [InlineData("dependencies.forbidden-packages", "{\"packages\":[null]}")]
    [InlineData("project.target-frameworks", "{\"allowed\":[\"\"]}")]
    [InlineData("syntax.no-goto", "null")]
    [InlineData("architecture.namespace-boundaries", "{}")]
    [InlineData("architecture.namespace-boundaries", "{\"from\":\"\"}")]
    [InlineData("architecture.namespace-boundaries", "{\"from\":1}")]
    [InlineData("architecture.namespace-boundaries", "{\"from\":\"Domain\"}")]
    [InlineData("dependencies.forbidden-usings", "{\"namespaces\":[\"System.IO\",\"\"]}")]
    public async Task Invalid_built_in_options_fail_explicitly(string id, string options)
    {
        using var document = JsonDocument.Parse(options);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new Analyzer().AnalyzeAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = [new(id, document.RootElement)], CompilationHasErrors = false,
        }));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData("performance.empty-array-allocation", "class Sample { int[] M() => new int[1]; }")]
    [InlineData("performance.empty-array-allocation", "class Sample { int[,] M() => new int[0, 0]; }")]
    [InlineData("performance.empty-array-allocation", "class Sample { int[][] M() => new int[1][]; }")]
    [InlineData("performance.empty-array-allocation", "class Sample { int[] M() => new int[1 - 1]; }")]
    [InlineData("performance.explicit-boxing", "class Sample { object? M() => (object?)null; }")]
    [InlineData("reliability.rethrow-caught-exception", "class Sample { void M() { try {} catch (System.Exception ex) { var other = new System.Exception(); throw other; } } }")]
    [InlineData("syntax.file-scoped-namespaces", "class Sample {}")]
    [InlineData("reliability.rethrow-caught-exception", "class Sample { void M(System.Exception ex) { throw ex; } }")]
    [InlineData("reliability.rethrow-caught-exception", "class Sample { void M() { try {} catch (System.Exception ex) { void R() { throw ex; } R(); } } }")]
    [InlineData("reliability.rethrow-caught-exception", "class Sample { void M() { try {} catch (System.Exception ex) { System.Action r = () => { throw ex; }; r(); } } }")]
    public async Task Policies_allow_other_constructs_and_respect_syntax_boundaries(string id, string source)
    {
        var config = Lint.Configure(c => c.Rule(id, JsonSerializer.SerializeToElement(new { })));
        Assert.Empty((await Analyze(config, source, Path.Combine(_directory, "Sample.cs"))).Diagnostics);
    }

    [Fact]
    public async Task Architecture_checks_references_inside_namespaces_but_does_not_report_namespace_declarations()
    {
        var config = Lint.Configure(c => c.Rule("architecture.namespace-boundaries",
            JsonSerializer.SerializeToElement(new { from = "Domain", to = new[] { "Domain.Infrastructure" } })));
        var path = Path.Combine(_directory, "Sample.cs");
        Assert.Empty((await Analyze(config, "namespace Domain { namespace Infrastructure { class Sample {} } }", path)).Diagnostics);
        var result = await Analyze(config,
            "namespace Domain.Infrastructure { class Service {} } namespace Domain { using Domain.Infrastructure; class Sample {} }", path);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task Default_type_member_limit_allows_twenty_and_reports_twenty_one()
    {
        var config = Lint.Configure(c => c.Rule("design.maximum-type-members", JsonSerializer.SerializeToElement(new { })));
        var members = string.Join(" ", Enumerable.Range(0, 20).Select(i => $"int field{i};"));
        var path = Path.Combine(_directory, "Sample.cs");
        Assert.Empty((await Analyze(config, $"class Sample {{ {members} }}", path)).Diagnostics);
        Assert.Single((await Analyze(config, $"class Sample {{ {members} int extra; }}", path)).Diagnostics);
    }

    [Fact]
    public async Task Package_policy_checks_central_versions_and_update_references_without_prefix_matches()
    {
        File.WriteAllText(Path.Combine(_directory, "Directory.Packages.props"),
            "<Project><ItemGroup><PackageVersion Update=\"Unsafe.Package\" Version=\"1.0\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(_directory, "Sample.csproj"),
            "<Project><ItemGroup><PackageReference Update=\"Unsafe.Package\"/><PackageReference Include=\"Unsafe.PackageExtra\"/><PackageReference /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"),
            "<Project><ItemGroup><PackageReference Include=\"Unsafe.Package\" /></ItemGroup></Project>");
        var result = await new Analyzer().AnalyzeAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [],
            Rules = [new("dependencies.forbidden-packages", JsonSerializer.SerializeToElement(new { packages = new[] { "Unsafe.Package" } }))],
        });
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Equal(new[] { "Directory.Packages.props", "Sample.csproj" }, result.Diagnostics.Select(d => Path.GetFileName(d.Location.FilePath)));
    }

    [Fact]
    public async Task Syntax_policies_work_without_compilations_and_keep_available_project_identity()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample { void M() { goto end; end: return; } }", path: "Sample.cs");
        var rule = new ConfiguredRule("syntax.no-goto", JsonSerializer.SerializeToElement(new { }));
        var analyzer = new Analyzer();
        var result = await analyzer.AnalyzeTreesAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = [rule],
        }, [tree]);
        Assert.Null(Assert.Single(result.Diagnostics).Project);
        var compilation = CSharpCompilation.Create("Sample", [tree], References);
        result = await analyzer.AnalyzeTreesAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], Rules = [rule], Compilations = [compilation],
            CompilationProjects = new Dictionary<Compilation, string> { [compilation] = "Sample.csproj" },
        }, [tree]);
        Assert.Equal("Sample.csproj", Assert.Single(result.Diagnostics).Project);
    }

    [Fact]
    public async Task Organization_reports_every_additional_type_at_its_declaration()
    {
        var tree = CSharpSyntaxTree.ParseText("class First {}\nrecord Second;\nenum Third {}", path: "Sample.cs");
        var result = await new Analyzer().AnalyzeTreesAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [],
            Rules = [new("organization.one-type-per-file", JsonSerializer.SerializeToElement(new { }))],
        }, [tree]);
        Assert.Equal(new[] { 2, 3 }, result.Diagnostics.Select(d => d.Location.Line));
    }

    [Fact]
    public async Task Namespace_options_compose_as_alternatives_and_ignore_unresolved_nameof_operator()
    {
        var config = Lint.Configure(c => c.Rule("dependencies.forbidden-usings",
            JsonSerializer.SerializeToElement(new { namespaces = new[] { "System.IO", "System.Net" } })));
        var path = Path.Combine(_directory, "Sample.cs");
        Assert.Single((await Analyze(config, "using System.IO; class Sample {}", path)).Diagnostics);
        config = Lint.Configure(c => c.Rule("architecture.namespace-boundaries",
            JsonSerializer.SerializeToElement(new { from = "Domain", to = new[] { "Infrastructure", "Other" } })));
        Assert.Single((await Analyze(config,
            "namespace Infrastructure { class Service {} } namespace Domain { class Sample { Infrastructure.Service M() => new(); } }", path)).Diagnostics);
        Assert.Empty((await Analyze(config, "namespace Domain { class Sample { string M() => nameof(Sample); } }", path)).Diagnostics);
    }

    [Fact]
    public async Task Semantic_policies_reject_a_claimed_error_free_context_without_the_matching_compilation()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample {}", path: "Sample.cs");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new Analyzer().AnalyzeTreesAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [], CompilationHasErrors = false,
            Rules = [new("performance.explicit-boxing", JsonSerializer.SerializeToElement(new { }))],
        }, [tree]));
        Assert.Contains("performance.explicit-boxing", error.Message);
        Assert.Contains("Sample.cs", error.Message);
    }

    [Theory]
    [InlineData("architecture.namespace-boundaries")]
    [InlineData("performance.explicit-boxing")]
    public async Task Semantic_policies_report_incomplete_analysis_when_compilation_is_unavailable(string id)
    {
        var result = await new Analyzer().AnalyzeAsync(new AnalysisContext
        {
            WorkingDirectory = _directory, NamingRules = [],
            Rules = [new(id, JsonSerializer.SerializeToElement(new { from = "Domain", to = new[] { "Infrastructure" } }))],
        });
        Assert.False(result.IsComplete);
        Assert.Single(result.Failures);
    }
}
