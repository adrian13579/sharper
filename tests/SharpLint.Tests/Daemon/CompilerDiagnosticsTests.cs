using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpLint.Analysis;
using SharpLint.Configuration;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;
using Diagnostic = SharpLint.Core.Diagnostics.Diagnostic;

namespace SharpLint.Tests.Daemon;

public sealed class CompilerDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-compiler-" + Guid.NewGuid().ToString("N"));
    private readonly SharpLintConfig _config = Lint.Configure(c => c.Naming(n => n.Classes().RequirePrefix("Expected").AsError()));
    public CompilerDiagnosticsTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    private WorkspaceCoordinator Create() => new(_root, _ => Task.FromResult(_config), watch: false);
    private async Task Equivalent(SnapshotResult incremental)
    {
        using var fresh = Create();
        var full = await fresh.CheckAsync(full: true);
        Assert.Equal(full.Result.Diagnostics, incremental.Result.Diagnostics);
        Assert.Equal(full.Result.Failures, incremental.Result.Failures);
        Assert.True(incremental.Validated);
    }

    [Theory]
    [InlineData("class Wrong { void M() { int x = 1 } }", "CS1002")]
    [InlineData("class Wrong { void M() { missing(); } }", "CS0103")]
    [InlineData("class Wrong { int M() => \"text\"; }", "CS0029")]
    [InlineData("class Wrong { void M() { M(1); } }", "CS1501")]
    [InlineData("class Wrong { Unknown value; }", "CS0246")]
    [InlineData("class Wrong { void M<T>() where T : struct {} void N() { M<string>(); } }", "CS0453")]
    [InlineData("class ExpectedA { private static int X; } class Wrong { int M() => ExpectedA.X; }", "CS0122")]
    public async Task Compiler_errors_and_unchanged_naming_diagnostics_are_reported_together(string source, string id)
    {
        Write("Source.cs", source);
        using var coordinator = Create();
        var first = await coordinator.CheckAsync();
        Assert.True(first.Result.HasErrors);
        Assert.True(first.Result.IsComplete);
        var diagnostic = Assert.Single(first.Result.Diagnostics, d => d.RuleId == id);
        Assert.Equal(DiagnosticOrigin.Compiler, diagnostic.Origin);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal("Compiler", diagnostic.CategoryId);
        Assert.Equal(Path.Combine(_root, "Source.cs"), diagnostic.Location.FilePath);
        Assert.Equal(1, diagnostic.Location.Line);
        Assert.True(diagnostic.Location.Column > 0);
        Assert.NotNull(diagnostic.Project);
        Assert.NotNull(diagnostic.SpanStart);
        Assert.NotNull(diagnostic.SpanLength);
        Assert.Contains(first.Result.Diagnostics, d => d.RuleId == "naming.class.prefix" && d.Origin == DiagnosticOrigin.SharpLint);
        var warm = await coordinator.CheckAsync();
        Assert.Equal(0, warm.Statistics.ProjectsAnalyzed);
        Assert.Equal(1, warm.Statistics.ProjectsReused);
        Assert.Equal(first.Result.Diagnostics, warm.Result.Diagnostics);
        await Equivalent(warm);
    }

    [Fact]
    public async Task Body_edits_cross_file_errors_fixes_and_concurrent_checks_invalidate_compiler_cache()
    {
        var api = Write("Api.cs", "public class ExpectedApi { public void Save() {} }");
        var consumer = Write("Consumer.cs", "class ExpectedConsumer { void M() { new ExpectedApi().Save(); } }");
        using var coordinator = Create();
        Assert.Empty((await coordinator.CheckAsync()).Result.Diagnostics);
        File.WriteAllText(consumer, "class ExpectedConsumer { void M() { missing(); } }");
        var checks = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => coordinator.CheckAsync()));
        Assert.Equal(1, checks.Sum(c => c.Statistics.ProjectsAnalyzed));
        Assert.All(checks, c => Assert.Contains(c.Result.Diagnostics, d => d.RuleId == "CS0103"));
        await Equivalent(checks[0]);
        File.WriteAllText(consumer, "class ExpectedConsumer { void M() { new ExpectedApi().Save(); } }");
        Assert.Empty((await coordinator.CheckAsync()).Result.Diagnostics);
        File.WriteAllText(api, "public class ExpectedApi { public void Persist() {} }");
        var changed = await coordinator.CheckAsync();
        Assert.Equal(1, changed.Statistics.DocumentsAnalyzed);
        Assert.Contains(changed.Result.Diagnostics, d => d.RuleId == "CS1061" && d.Location.FilePath == consumer);
        await Equivalent(changed);
    }

    [Fact]
    public void Adapter_preserves_precise_physical_coordinates_and_supports_no_location_and_all_severities()
    {
        var tree = CSharpSyntaxTree.ParseText("// header\nclass C { int M() => missing; }", path: "/repo/C.cs");
        var compilation = CSharpCompilation.Create("test", [tree]);
        var roslyn = Assert.Single(compilation.GetDiagnostics(), d => d.Id == "CS0103");
        var converted = CompilerDiagnosticAdapter.Convert(roslyn, "test-project");
        Assert.Equal(new SourceLocation("/repo/C.cs", 2, 22), converted.Location);
        Assert.Equal(roslyn.Location.SourceSpan.Start, converted.SpanStart);
        Assert.Equal(7, converted.SpanLength);
        foreach (var severity in Enum.GetValues<DiagnosticSeverity>())
        {
            var descriptor = new DiagnosticDescriptor("CS9999", "title", "message", "Compiler", severity, true,
                helpLinkUri: "https://example.test/help");
            var noLocation = CompilerDiagnosticAdapter.Convert(Microsoft.CodeAnalysis.Diagnostic.Create(descriptor, Location.None), "P");
            Assert.False(noLocation.HasSourceLocation);
            Assert.Null(noLocation.Location.FilePath);
            Assert.Null(noLocation.SpanStart);
            Assert.Null(noLocation.SpanLength);
            Assert.Equal("https://example.test/help", noLocation.HelpUrl);
            Assert.Equal(severity.ToString(), noLocation.Severity.ToString());
            Assert.Equal("message", noLocation.Message);
            Assert.Equal(noLocation, JsonSerializer.Deserialize<Diagnostic>(JsonSerializer.Serialize(noLocation)));
        }
    }

    [Fact]
    public void Aggregation_removes_only_exact_duplicates_and_orders_distinct_project_contexts()
    {
        var a = new Diagnostic("CS0103", Severity.Error, "missing", new SourceLocation("/repo/A.cs", 1, 1))
        { Origin = DiagnosticOrigin.Compiler, Project = "A" };
        var b = a with { Project = "B" };
        var c = a with { RuleId = "CS0246" };
        var expected = new[] { a, c, b };
        Assert.Equal(expected, DiagnosticAggregator.Aggregate([b, c, a, a]));
        Assert.Equal(expected, DiagnosticAggregator.Aggregate([a, c, b, c]));
        var slash = a with { Location = new SourceLocation("C:/repo/A.cs", 1, 1) };
        var backslash = a with { Location = new SourceLocation("C:\\repo\\A.cs", 1, 1) };
        Assert.Equal(DiagnosticAggregator.Aggregate([slash, backslash]), DiagnosticAggregator.Aggregate([backslash, slash]));
    }

    private async Task Project(string name, string extra = "")
    {
        var path = Write($"{name}/{name}.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              {{extra}}
            </Project>
            """);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "restore", path, "--ignore-failed-sources", "-p:NuGetAudit=false" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    [Fact]
    public async Task Dependent_projects_linked_files_and_unaffected_projects_match_fresh_compilations()
    {
        Write("Shared.cs", "public class ExpectedShared { public int M() => missing; }");
        var api = Write("Api/Api.cs", "public interface IApi { void Save(); }");
        Write("App/App.cs", "public class ExpectedApp { public void M(IApi api) => api.Save(); }");
        await Project("Api", "<ItemGroup><Compile Include=\"../Shared.cs\" /></ItemGroup>");
        await Project("App", "<ItemGroup><ProjectReference Include=\"../Api/Api.csproj\" /><Compile Include=\"../Shared.cs\" /></ItemGroup>");
        await Project("Other");
        using var coordinator = Create();
        var first = await coordinator.CheckAsync();
        Assert.Equal(2, first.Result.Diagnostics.Count(d => d.RuleId == "CS0103"));
        Assert.Equal(2, first.Result.Diagnostics.Where(d => d.RuleId == "CS0103").Select(d => d.Project).Distinct().Count());
        await Equivalent(first);
        var warm = await coordinator.CheckAsync();
        Assert.Equal(3, warm.Statistics.ProjectsReused);
        Assert.Equal(0, warm.Statistics.ProjectsAnalyzed);
        File.WriteAllText(api, "public interface IApi { void Persist(); }");
        var changed = await coordinator.CheckAsync();
        Assert.Equal(2, changed.Statistics.ProjectsAnalyzed);
        Assert.Equal(1, changed.Statistics.ProjectsReused);
        Assert.Contains(changed.Result.Diagnostics, d => d.RuleId == "CS1061" && d.Project!.Contains("App.csproj"));
        await Equivalent(changed);
        Write("Shared.cs", "public class ExpectedShared { public int M() => 1; }");
        var fixedShared = await coordinator.CheckAsync();
        Assert.DoesNotContain(fixedShared.Result.Diagnostics, d => d.RuleId == "CS0103");
        Assert.Equal(2, fixedShared.Statistics.ProjectsAnalyzed);
        await Equivalent(fixedShared);
        var appProject = Path.Combine(_root, "App/App.csproj");
        File.WriteAllText(appProject, File.ReadAllText(appProject).Replace("<ProjectReference Include=\"../Api/Api.csproj\" />", ""));
        var removedReference = await coordinator.CheckAsync();
        Assert.Contains(removedReference.Result.Diagnostics, d => d.RuleId == "CS0246");
        await Equivalent(removedReference);
    }

    [Fact]
    public async Task Project_options_warning_policy_imports_and_locationless_diagnostics_are_honored()
    {
        Write("App/App.cs", "class ExpectedApp { void M() { int unused; } }");
        var props = Write("Settings.props", "<Project><PropertyGroup><NoWarn>CS0168</NoWarn></PropertyGroup></Project>");
        await Project("App", "<Import Project=\"../Settings.props\" />");
        using var coordinator = Create();
        var initial = await coordinator.CheckAsync();
        Assert.DoesNotContain(initial.Result.Diagnostics, d => d.RuleId == "CS0168");
        File.WriteAllText(props, "<Project />");
        var warning = await coordinator.CheckAsync();
        Assert.Equal(Severity.Warning, Assert.Single(warning.Result.Diagnostics, d => d.RuleId == "CS0168").Severity);
        Assert.False(warning.Result.HasErrors);
        await Equivalent(warning);
        File.WriteAllText(props, "<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors><OutputType>Exe</OutputType></PropertyGroup></Project>");
        var error = await coordinator.CheckAsync();
        Assert.Equal(Severity.Error, Assert.Single(error.Result.Diagnostics, d => d.RuleId == "CS0168").Severity);
        Assert.False(Assert.Single(error.Result.Diagnostics, d => d.RuleId == "CS5001").HasSourceLocation);
        await Equivalent(error);
        File.WriteAllText(props, "<Project><PropertyGroup><WarningLevel>0</WarningLevel></PropertyGroup></Project>");
        var quiet = await coordinator.CheckAsync();
        Assert.DoesNotContain(quiet.Result.Diagnostics, d => d.RuleId == "CS0168");
        await Equivalent(quiet);
    }

    [Fact]
    public async Task Failed_project_reload_preserves_published_snapshot_and_recovers()
    {
        await Project("App");
        using var coordinator = Create();
        await coordinator.CheckAsync();
        var snapshot = coordinator.CurrentSnapshot;
        var project = Path.Combine(_root, "App/App.csproj");
        var original = File.ReadAllText(project);
        File.WriteAllText(project, "<broken");
        await Assert.ThrowsAnyAsync<Exception>(() => coordinator.CheckAsync());
        Assert.Same(snapshot, coordinator.CurrentSnapshot);
        File.WriteAllText(project, original);
        await Equivalent(await coordinator.CheckAsync());
    }

    [Fact]
    public async Task Unsafe_module_is_reported_as_incomplete_without_losing_compiler_or_naming_findings()
    {
        Write("Source.cs", "class Wrong { int M() => missing; }");
        var module = new ModuleAnalyzer();
        var definition = new RuleDefinition("test.semantic", "design", RuleScope.Project)
        { InputRequirement = RuleInputRequirement.ErrorFreeCompilation };
        var registry = new RuleRegistry([new("test", [], [new(definition, module)])]);
        var config = _config with { Rules = [new("test.semantic", JsonSerializer.SerializeToElement(new { }))] };
        using var coordinator = new WorkspaceCoordinator(_root, _ => Task.FromResult(config), registry, watch: false);
        var result = await coordinator.CheckAsync();
        Assert.False(result.Result.IsComplete);
        Assert.Equal("test.semantic", Assert.Single(result.Result.Failures).RuleId);
        Assert.Equal(0, module.Calls);
        Assert.Contains(result.Result.Diagnostics, d => d.RuleId == "CS0103");
        Assert.Contains(result.Result.Diagnostics, d => d.RuleId == "naming.class.prefix");
        Write("Source.cs", "class Wrong { int M() => 1; }");
        var recovered = await coordinator.CheckAsync();
        Assert.True(recovered.Result.IsComplete);
        Assert.Equal(1, module.Calls);
        Assert.DoesNotContain(recovered.Result.Diagnostics, d => d.Origin == DiagnosticOrigin.Compiler);
    }

    private sealed class ModuleAnalyzer : IRuleAnalyzer
    {
        public int Calls { get; private set; }
        public ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyList<RuleViolation>>([]);
        }
    }

    [Fact]
    public async Task External_compiler_ruleset_changes_recompute_effective_warning_severity()
    {
        var path = _root + ".ruleset";
        var included = _root + ".included.ruleset";
        const string policy = """
            <RuleSet Name="Policy" Description="Test" ToolsVersion="15.0">
              <Rules AnalyzerId="Microsoft.CodeAnalysis.CSharp" RuleNamespace="Microsoft.CodeAnalysis.CSharp">
                <Rule Id="CS0168" Action="Warning" />
              </Rules>
            </RuleSet>
            """;
        try
        {
            File.WriteAllText(path, $"<RuleSet Name=\"Root\" Description=\"Test\" ToolsVersion=\"15.0\"><Include Path=\"{included}\" Action=\"Default\" /></RuleSet>");
            File.WriteAllText(included, policy);
            Write("App/App.cs", "class ExpectedApp { void M() { int unused; } }");
            await Project("App", $"<PropertyGroup><CodeAnalysisRuleSet>{path}</CodeAnalysisRuleSet></PropertyGroup>");
            using var coordinator = Create();
            var warning = await coordinator.CheckAsync();
            Assert.Equal(Severity.Warning, Assert.Single(warning.Result.Diagnostics).Severity);
            File.WriteAllText(included, policy.Replace("Action=\"Warning\"", "Action=\"Error\""));
            var error = await coordinator.CheckAsync();
            Assert.Equal(Severity.Error, Assert.Single(error.Result.Diagnostics).Severity);
            await Equivalent(error);
            File.WriteAllText(included, policy.Replace("Action=\"Warning\"", "Action=\"None\""));
            var suppressed = await coordinator.CheckAsync();
            Assert.Empty(suppressed.Result.Diagnostics);
            await Equivalent(suppressed);
        }
        finally { File.Delete(path); File.Delete(included); }
    }

    [Fact]
    public async Task Optional_external_import_creation_and_generated_source_edits_invalidate_the_snapshot()
    {
        var external = _root + ".props";
        try
        {
            var generated = Write("App/obj/Generated.cs", "class Wrong { void M() { int unused; } }");
            await Project("App", $"<Import Project=\"{external}\" Condition=\"Exists('{external}')\" />"
                + "<ItemGroup><Compile Include=\"obj/Generated.cs\" /></ItemGroup>");
            using var coordinator = Create();
            var warning = await coordinator.CheckAsync();
            Assert.Contains(warning.Result.Diagnostics, d => d.RuleId == "CS0168");
            Assert.DoesNotContain(warning.Result.Diagnostics, d => d.Origin == DiagnosticOrigin.SharpLint);
            File.WriteAllText(external, "<Project><PropertyGroup><NoWarn>CS0168</NoWarn></PropertyGroup></Project>");
            var quiet = await coordinator.CheckAsync();
            Assert.DoesNotContain(quiet.Result.Diagnostics, d => d.RuleId == "CS0168");
            await Equivalent(quiet);
            File.WriteAllText(generated, "class Wrong { int M() => missing; }");
            var error = await coordinator.CheckAsync();
            Assert.Contains(error.Result.Diagnostics, d => d.RuleId == "CS0103" && d.Location.FilePath == generated);
            await Equivalent(error);
            File.Delete(external);
            await Equivalent(await coordinator.CheckAsync());
        }
        finally { File.Delete(external); }
    }

    [Fact]
    public async Task Language_version_nullable_and_metadata_binary_contents_are_compilation_inputs()
    {
        var source = Write("App/App.cs", "public class ExpectedApp { public string? Name { get => field; set => field = value; } }");
        await Project("App", "<PropertyGroup><Nullable>enable</Nullable><LangVersion>14.0</LangVersion></PropertyGroup>");
        using var coordinator = Create();
        var modern = await coordinator.CheckAsync();
        Assert.Empty(modern.Result.Diagnostics);
        var project = Path.Combine(_root, "App/App.csproj");
        var settings = File.ReadAllText(project);
        File.WriteAllText(project, settings.Replace("14.0", "13.0"));
        var older = await coordinator.CheckAsync();
        Assert.Contains(older.Result.Diagnostics, d => d.Origin == DiagnosticOrigin.Compiler && d.Severity == Severity.Error);
        await Equivalent(older);
        File.WriteAllText(project, settings);
        File.WriteAllText(source, "public class ExpectedApp { public string Name => null; }");
        var nullable = await coordinator.CheckAsync();
        Assert.Contains(nullable.Result.Diagnostics, d => d.RuleId == "CS8603");
        await Equivalent(nullable);

        var binary = Path.Combine(_root, "Api.dll");
        var frameworkReferences = coordinator.CurrentSnapshot!.Solution.Projects.First(p => p.FilePath is not null).MetadataReferences;
        void Emit(string method)
        {
            var compilation = CSharpCompilation.Create("Api", [CSharpSyntaxTree.ParseText($"public class Api {{ public void {method}() {{ }} }}")],
                frameworkReferences, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = File.Create(binary);
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("; ", emitted.Diagnostics));
        }
        Emit("Save");
        File.WriteAllText(source, "public class ExpectedApp { public void M() => new Api().Save(); }");
        File.WriteAllText(project, settings.Replace("</Project>", "<ItemGroup><Reference Include=\"Api\"><HintPath>../Api.dll</HintPath></Reference></ItemGroup></Project>"));
        Assert.Empty((await coordinator.CheckAsync()).Result.Diagnostics);
        var timestamp = File.GetLastWriteTimeUtc(binary);
        var length = new FileInfo(binary).Length;
        Emit("Gone");
        File.SetLastWriteTimeUtc(binary, timestamp);
        Assert.Equal(length, new FileInfo(binary).Length);
        var changed = await coordinator.CheckAsync();
        Assert.Contains(changed.Result.Diagnostics, d => d.RuleId == "CS1061");
        await Equivalent(changed);
    }
}
