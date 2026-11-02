using SharpLint.Analysis;

namespace SharpLint.Tests.Lsp;

public sealed class ServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-service-" + Guid.NewGuid().ToString("N"));
    public ServiceTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Sample.cs"), "missing");
        File.WriteAllText(Path.Combine(_root, "Test.csproj"), "project");
    }
    public void Dispose() => Directory.Delete(_root, true);
    private DiagnosticService Create(string mode = "normal", double seconds = 15)
    {
        File.WriteAllText(Path.Combine(_root, "mode.txt"), mode);
        return new(new DiagnosticServiceOptions { RootPath = _root, ServerPath = Path.Combine(RepositoryRoot(), "tests/SharpLint.FakeServer/bin/Debug/net10.0/SharpLint.FakeServer.dll"), Timeout = TimeSpan.FromSeconds(seconds) });
    }

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SharpLint.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static async Task<DiagnosticResponse> CheckStableAsync(DiagnosticService service)
    {
        DiagnosticResponse response;
        for (var attempt = 0; ; attempt++)
        {
            response = await service.CheckAsync();
            if (response.Status != "incomplete" || attempt == 2) break;
            await Task.Delay(100);
        }
        Assert.True(response.Complete, string.Join(";", response.Errors));
        return response;
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("full-sync")]
    public async Task Persistent_session_handles_unchanged_fix_and_concurrent_checks(string mode)
    {
        await using var service = Create(mode);
        var first = await service.CheckAsync("Sample.cs");
        Assert.True(first.Complete, string.Join(";", first.Errors));
        Assert.Equal("verified", first.Freshness);
        Assert.Equal("CS0103", Assert.Single(first.Diagnostics).Code);
        var unchanged = await service.CheckAsync("Sample.cs");
        Assert.Equal(first.Diagnostics, unchanged.Diagnostics);
        Assert.True(unchanged.Complete);
        var status = await service.StatusAsync();
        Assert.True(status.Available);
        Assert.Equal(1, status.SessionStarts);
        File.WriteAllText(Path.Combine(_root, "Sample.cs"), "fixed");
        var calls = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => service.CheckAsync("Sample.cs")));
        Assert.All(calls, result => { Assert.True(result.Complete, string.Join(";", result.Errors)); Assert.Empty(result.Diagnostics); });
        Assert.Equal(status.ProcessId, (await service.StatusAsync()).ProcessId);
    }

    [Theory]
    [InlineData("mutate")]
    [InlineData("delete")]
    [InlineData("project-mutate")]
    public async Task Concurrent_disk_changes_never_report_fresh_results(string mode)
    {
        await using var service = Create(mode);
        var response = await service.CheckAsync();
        Assert.Equal("incomplete", response.Status);
        Assert.False(response.Complete);
        Assert.Equal("uncertain", response.Freshness);
        Assert.Contains(response.Errors, e => e.Contains("changed during analysis", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("crash-start")]
    [InlineData("crash-check")]
    [InlineData("malformed")]
    [InlineData("unchanged-without-cache")]
    public async Task Protocol_failures_are_explicit(string mode)
    {
        await using var service = Create(mode);
        var result = await service.CheckAsync();
        Assert.Equal("failed", result.Status);
        Assert.False(result.Complete);
        Assert.Equal("uncertain", result.Freshness);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("push-only")]
    [InlineData("unknown-document")]
    [InlineData("load-error")]
    [InlineData("load-error-request")]
    [InlineData("refresh-always")]
    [InlineData("unregister-diagnostics")]
    public async Task Unsupported_or_unconfirmed_analysis_is_incomplete(string mode)
    {
        await using var service = Create(mode);
        var result = await service.CheckAsync();
        Assert.Equal("incomplete", result.Status);
        Assert.False(result.Complete);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Initial_refresh_is_followed_by_a_bounded_fresh_pull()
    {
        await using var service = Create("refresh-once");
        var result = await service.CheckAsync("Sample.cs");
        Assert.True(result.Complete, string.Join(";", result.Errors));
        Assert.Equal("verified", result.Freshness);
        Assert.Equal("CS0103", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Old_pushes_do_not_replace_current_pull_diagnostics()
    {
        await using var service = Create("stale-push");
        var result = await service.CheckAsync("Sample.cs");
        Assert.True(result.Complete, string.Join(";", result.Errors));
        Assert.Equal("CS0103", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("hang-start")]
    [InlineData("hang-check")]
    public async Task Timeout_is_bounded_and_distinct_from_cancellation(string mode)
    {
        await using var service = Create(mode, 1);
        var response = await service.CheckAsync();
        Assert.Equal("timedOut", response.Status);
        Assert.False(response.Complete);
        Assert.Equal("uncertain", response.Freshness);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal("cancelled", (await service.CheckAsync(cancellationToken: cancellation.Token)).Status);
    }

    [Fact]
    public async Task New_deleted_renamed_and_project_changes_reload_the_session()
    {
        await using var service = Create();
        await CheckStableAsync(service);
        var first = await service.StatusAsync();
        File.WriteAllText(Path.Combine(_root, "New.cs"), "fixed");
        await CheckStableAsync(service);
        Assert.Equal(first.SessionStarts + 1, (await service.StatusAsync()).SessionStarts);
        File.Move(Path.Combine(_root, "New.cs"), Path.Combine(_root, "Renamed.cs"));
        await CheckStableAsync(service);
        File.Delete(Path.Combine(_root, "Renamed.cs"));
        await CheckStableAsync(service);
        File.AppendAllText(Path.Combine(_root, "Test.csproj"), "changed");
        await CheckStableAsync(service);
        Assert.Equal(first.SessionStarts + 4, (await service.StatusAsync()).SessionStarts);
    }

    [Fact]
    public async Task Invalid_scope_missing_server_and_empty_scope_fail_clearly()
    {
        await using var service = Create();
        Assert.Equal("failed", (await service.CheckAsync("../outside.cs")).Status);
        File.Delete(Path.Combine(_root, "Sample.cs"));
        Assert.Equal("failed", (await service.CheckAsync()).Status);
        await using var unavailable = new DiagnosticService(new DiagnosticServiceOptions { RootPath = _root, ServerPath = Path.Combine(_root, "does-not-exist") });
        var status = await unavailable.StatusAsync();
        Assert.False(status.Available);
        Assert.Contains("not found", status.LastError);
    }

    [Fact]
    public async Task Multiple_solutions_require_selection_and_status_reflects_roslyn_settings()
    {
        File.WriteAllText(Path.Combine(_root, "First.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_root, "Second.slnx"), "<Solution />");
        await using (var ambiguous = Create())
        {
            var status = await ambiguous.StatusAsync();
            Assert.False(status.Available);
            Assert.Contains("Multiple solutions", status.LastError);
        }
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
        File.WriteAllText(Path.Combine(_root, ".vscode", "settings.json"), "{ // editor settings\n\"dotnet.defaultSolution\": \"Second.slnx\", }");
        await using var selected = Create();
        var selectedStatus = await selected.StatusAsync();
        Assert.True(selectedStatus.Available, selectedStatus.LastError);
        Assert.Equal(Path.Combine(_root, "Second.slnx"), selectedStatus.Solution);
    }

    [Fact]
    public async Task Active_cancellation_disposes_the_uncertain_server_process()
    {
        await using var service = Create("hang-check");
        var status = await service.StatusAsync();
        Assert.True(status.Available);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var response = await service.CheckAsync(cancellationToken: cancellation.Token);
        Assert.Equal("cancelled", response.Status);
        Assert.False(response.Complete);
        AssertProcessExited(status.ProcessId!.Value);
    }

    [Fact]
    public async Task A_cancelled_queued_request_does_not_dispose_the_active_session()
    {
        await using var service = Create("slow-check");
        var initial = await service.StatusAsync();
        var first = service.CheckAsync("Sample.cs");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var second = service.CheckAsync("Sample.cs", cancellation.Token);
        Assert.Equal("cancelled", (await second).Status);
        Assert.True((await first).Complete);
        Assert.Equal(initial.ProcessId, (await service.StatusAsync()).ProcessId);
    }

    [Fact]
    public async Task Graceful_shutdown_reaps_the_persistent_server_process()
    {
        var service = Create();
        var status = await service.StatusAsync();
        await service.DisposeAsync();
        AssertProcessExited(status.ProcessId!.Value);
    }

    private static void AssertProcessExited(int id)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(id);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException) { /* Already reaped. */ }
    }
}
