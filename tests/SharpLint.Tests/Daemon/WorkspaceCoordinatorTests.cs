using System.Text.Json;
using Microsoft.CodeAnalysis;
using SharpLint.Analysis;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;
using Diagnostic = SharpLint.Core.Diagnostics.Diagnostic;

namespace SharpLint.Tests.Daemon;

public sealed class WorkspaceCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-snapshot-" + Guid.NewGuid().ToString("N"));
    private SharpLintConfig _config = Lint.Configure(c => c.Naming(n =>
    {
        n.Classes().RequireConvention(NamingConvention.PascalCase).AsError();
        n.Methods().RequirePrefix("Do");
        n.Fields().RequirePrefix("_");
    }));

    public WorkspaceCoordinatorTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Write(string name, string source)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, source);
        return path;
    }
    private WorkspaceCoordinator Create(bool watch = false) => new(_root, _ => Task.FromResult(_config), watch: watch);

    private async Task AssertFullEquivalent(SnapshotResult incremental)
    {
        using var fresh = Create();
        var full = await fresh.CheckAsync(full: true);
        Assert.Equal(full.Result.Diagnostics.ToArray(), incremental.Result.Diagnostics.ToArray());
        Assert.Equal(full.Result.FilesAnalyzed, incremental.Result.FilesAnalyzed);
        Assert.True(incremental.Validated);
    }

    [Fact]
    public async Task Differential_edits_additions_deletions_renames_and_configuration_match_fresh_analysis()
    {
        var a = Write("A.cs", "class bad { void wrong() {} int field; }");
        var b = Write("B.cs", "class Good { void DoWork() {} int _field; }");
        using var coordinator = Create(); // No watcher: reconciliation must detect every change independently.
        var cold = await coordinator.CheckAsync();
        Assert.Equal(2, cold.Statistics.DocumentsAnalyzed);
        await AssertFullEquivalent(cold);
        var solution = coordinator.CurrentSnapshot!.Solution;
        var tree = await solution.Projects.Single().Documents.Single(d => d.FilePath == b).GetSyntaxTreeAsync();
        var warm = await coordinator.CheckAsync();
        Assert.Equal(cold.SnapshotVersion, warm.SnapshotVersion);
        Assert.Equal(0, warm.Statistics.DocumentsAnalyzed);
        Assert.Equal(2, warm.Statistics.CacheHits);
        Assert.Same(solution, coordinator.CurrentSnapshot.Solution);
        await AssertFullEquivalent(warm);

        foreach (var source in new[] { "class Better { void wrong() {} int field; }",
                     "class Better { void DoWork() {} int field; }", "class Better { void DoWork() {} int _field; }",
                     "class bad { void other() {} int extra; void DoAdded() {} }", "class Better {}" })
        {
            File.WriteAllText(a, source);
            var changed = await coordinator.CheckAsync();
            Assert.Equal(1, changed.Statistics.DocumentsAnalyzed);
            Assert.Equal(1, changed.Statistics.DocumentsReused);
            Assert.True(changed.SnapshotVersion > warm.SnapshotVersion);
            Assert.Same(tree, await coordinator.CurrentSnapshot.Solution.GetDocument(solution.Projects.Single()
                .Documents.Single(d => d.FilePath == b).Id)!.GetSyntaxTreeAsync());
            await AssertFullEquivalent(changed);
        }
        var added = Write("C.cs", "class bad {}");
        await AssertFullEquivalent(await coordinator.CheckAsync());
        File.Move(added, Path.Combine(_root, "Renamed.cs"));
        var renamed = await coordinator.CheckAsync();
        Assert.Equal(1, renamed.Statistics.DocumentsAnalyzed);
        Assert.All(renamed.Result.Diagnostics.Where(d => d.Origin == DiagnosticOrigin.SharpLint), d => Assert.EndsWith("Renamed.cs", d.Location.FilePath));
        await AssertFullEquivalent(renamed);
        File.Delete(Path.Combine(_root, "Renamed.cs"));
        var deleted = await coordinator.CheckAsync();
        Assert.Equal(0, deleted.Statistics.DocumentsAnalyzed);
        Assert.DoesNotContain(deleted.Result.Diagnostics, d => d.Origin == DiagnosticOrigin.SharpLint);
        await AssertFullEquivalent(deleted);
        _config = Lint.Configure(c => c.Naming(n => n.Classes().RequirePrefix("Expected").AsError()));
        Write("sharplint.config.cs", "// configuration changed");
        var configured = await coordinator.CheckAsync();
        Assert.Equal(3, configured.Statistics.DocumentsAnalyzed);
        Assert.Equal(2, configured.Result.Diagnostics.Count(d => d.Origin == DiagnosticOrigin.SharpLint));
        await AssertFullEquivalent(configured);
        var full = await coordinator.CheckAsync(full: true);
        Assert.Equal(3, full.Statistics.DocumentsAnalyzed);
        Assert.Equal(0, full.Statistics.DocumentsReused);
        await AssertFullEquivalent(full);
    }

    [Fact]
    public async Task Project_inputs_invalidate_all_documents_and_content_hashes_ignore_timestamps()
    {
        var path = Write("A.cs", "class bad {}");
        Write("B.cs", "class Other {}");
        using var coordinator = Create();
        var initial = await coordinator.CheckAsync();
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "class Sad {}");
        File.SetLastWriteTimeUtc(path, timestamp);
        var edit = await coordinator.CheckAsync();
        Assert.Equal(1, edit.Statistics.DocumentsAnalyzed);
        Assert.Empty(edit.Result.Diagnostics);
        Write("Directory.Build.props", "<Project />");
        var project = await coordinator.CheckAsync();
        Assert.Equal(2, project.Statistics.DocumentsAnalyzed);
        Assert.NotEqual(initial.InputFingerprint, project.InputFingerprint);
        Assert.Equal(0, project.Statistics.CacheHits);
        await AssertFullEquivalent(project);
    }

    [Fact]
    public async Task Concurrent_equivalent_requests_analyze_each_document_once_and_cancellation_does_not_poison_cache()
    {
        Write("A.cs", "class bad {}");
        using var coordinator = Create(watch: true);
        var checks = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => coordinator.CheckAsync()));
        Assert.Equal(1, checks.Sum(check => check.Statistics.DocumentsAnalyzed));
        Assert.Single(checks.Select(check => check.SnapshotVersion).Distinct());
        Assert.All(checks, check => Assert.Equal(checks[0].Result.Diagnostics, check.Result.Diagnostics));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.CheckAsync(cancellationToken: cancellation.Token));
        Assert.Equal(1, (await coordinator.CheckAsync()).Statistics.CacheHits);
    }

    [Fact]
    public async Task Change_during_analysis_retries_and_never_publishes_mixed_results()
    {
        var path = Write("A.cs", "class bad {}");
        var loads = 0;
        using var coordinator = new WorkspaceCoordinator(_root, _ =>
        {
            if (++loads == 1) File.WriteAllText(path, "class Good {}");
            return Task.FromResult(_config);
        }, watch: false);
        var result = await coordinator.CheckAsync();
        Assert.Equal(2, loads);
        Assert.Equal(1, result.SnapshotVersion);
        Assert.Empty(result.Result.Diagnostics);
        await AssertFullEquivalent(result);
    }

    [Fact]
    public async Task Continuous_changes_fail_explicitly_and_configuration_failure_preserves_published_snapshot()
    {
        var path = Write("A.cs", "class bad {}");
        var loads = 0;
        var unstable = false;
        using var coordinator = new WorkspaceCoordinator(_root, _ =>
        {
            if (unstable) File.WriteAllText(path, $"class Changed{++loads} {{}}");
            return Task.FromResult(_config);
        }, watch: false);
        await coordinator.CheckAsync();
        var snapshot = coordinator.CurrentSnapshot;
        unstable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CheckAsync(full: true));
        Assert.Same(snapshot, coordinator.CurrentSnapshot);
        Assert.Equal(3, loads);
        unstable = false;
        await AssertFullEquivalent(await coordinator.CheckAsync());
    }

    [Fact]
    public async Task Failed_configuration_does_not_return_cached_success_and_recovers_without_restart()
    {
        Write("A.cs", "class bad {}");
        var fail = false;
        using var coordinator = new WorkspaceCoordinator(_root, _ => fail
            ? throw new InvalidOperationException("config failure marker") : Task.FromResult(_config), watch: false);
        await coordinator.CheckAsync();
        var published = coordinator.CurrentSnapshot;
        Write("sharplint.config.cs", "// changed config");
        fail = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CheckAsync());
        Assert.Equal("config failure marker", error.Message);
        Assert.Same(published, coordinator.CurrentSnapshot);
        fail = false;
        await AssertFullEquivalent(await coordinator.CheckAsync());
    }

    [Fact]
    public async Task Missing_module_registration_is_an_error_even_with_cached_naming_results()
    {
        Write("A.cs", "class Good {}");
        using var coordinator = Create();
        await coordinator.CheckAsync();
        var published = coordinator.CurrentSnapshot;
        _config = _config with { Rules = [new ConfiguredRule("missing.rule", JsonSerializer.SerializeToElement(new { }), Severity.Hidden)] };
        Write("sharplint.config.cs", "// changed config");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CheckAsync());
        Assert.Contains("no registered analyzer", error.Message);
        Assert.Same(published, coordinator.CurrentSnapshot);
    }

    [Fact]
    public async Task Duplicate_and_rapid_writes_with_identical_final_contents_reuse_cache()
    {
        var path = Write("A.cs", "class bad {}");
        using var coordinator = Create();
        var first = await coordinator.CheckAsync();
        for (var i = 0; i < 10; i++) File.WriteAllText(path, "class Good {}");
        File.WriteAllText(path, "class bad {}");
        var next = await coordinator.CheckAsync();
        Assert.Equal(first.SnapshotVersion, next.SnapshotVersion);
        Assert.Equal(1, next.Statistics.CacheHits);
        await AssertFullEquivalent(next);
    }

    [Fact]
    public async Task Unknown_module_dependencies_always_execute_against_complete_snapshot()
    {
        Write("A.cs", "class Good {}");
        Write("B.cs", "class Other {}");
        var module = new CountingAnalyzer();
        var registry = new RuleRegistry([new RuleModule("test", [],
            [new RuleRegistration(new RuleDefinition("test.rule", SharpLint.Core.Rules.Rules.Design.Id, RuleScope.SourceFile), module)])]);
        _config = _config with { Rules = [new ConfiguredRule("test.rule", JsonSerializer.SerializeToElement(new { }))] };
        using var coordinator = new WorkspaceCoordinator(_root, _ => Task.FromResult(_config), registry, watch: false);
        await coordinator.CheckAsync();
        var warm = await coordinator.CheckAsync();
        Assert.Equal(2, module.Calls);
        Assert.Equal(2, module.Documents);
        Assert.Equal(2, warm.Statistics.CacheHits);
        Assert.Single(warm.Result.Diagnostics);
    }

    private sealed class CountingAnalyzer : IRuleAnalyzer
    {
        public int Calls { get; private set; }
        public int Documents { get; private set; }
        public ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken)
        {
            Calls++;
            Documents = context.SyntaxTrees.Count;
            return ValueTask.FromResult<IReadOnlyList<RuleViolation>>([new("module", new SourceLocation(context.SyntaxTrees[0].FilePath, 1, 1))]);
        }
    }
}
