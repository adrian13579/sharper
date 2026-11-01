using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpLint.Tests.Cli;

public sealed partial class CheckCommandFunctionalTests
{
    [Fact]
    public async Task Daemon_incremental_sequence_matches_independent_full_checks_and_restarts()
    {
        WriteConfig("n.Classes().RequirePrefix(\"Expected\").AsError();");
        await WriteProjectAsync();
        var path = Path.Combine(_workDir, "A.cs");
        File.WriteAllText(path, "class Wrong {}");
        File.WriteAllText(Path.Combine(_workDir, "B.cs"), "class ExpectedGood {}");
        Assert.Contains("stopped", (await RunCliAsync("daemon", "status")).Stdout);
        var first = await CheckWithStats();
        Assert.Equal(3, first.GetProperty("statistics").GetProperty("documentsAnalyzed").GetInt32());
        var warm = await CheckWithStats();
        Assert.Equal(3, warm.GetProperty("statistics").GetProperty("documentsReused").GetInt32());
        Assert.Equal(0, warm.GetProperty("statistics").GetProperty("documentsAnalyzed").GetInt32());
        Assert.Contains("running", (await RunCliAsync("daemon", "status")).Stdout);

        foreach (var source in new[] { "class AnotherWrong {}", "class ExpectedFixed {}" })
        {
            File.WriteAllText(path, source);
            var changed = await CheckWithStats();
            Assert.Equal(1, changed.GetProperty("statistics").GetProperty("documentsAnalyzed").GetInt32());
            await AssertMatchesFull(changed);
        }
        var added = Path.Combine(_workDir, "Added.cs");
        File.WriteAllText(added, "class Wrong {}");
        await AssertMatchesFull(await CheckWithStats());
        File.Move(added, Path.Combine(_workDir, "Renamed.cs"));
        var renamed = await CheckWithStats();
        Assert.EndsWith("Renamed.cs", Assert.Single(renamed.GetProperty("diagnostics").EnumerateArray()).GetProperty("file").GetString());
        await AssertMatchesFull(renamed);
        File.Delete(Path.Combine(_workDir, "Renamed.cs"));
        Assert.Empty((await CheckWithStats()).GetProperty("diagnostics").EnumerateArray());
        WriteConfig("n.Classes().RequirePrefix(\"Different\").AsError();");
        var configured = await CheckWithStats();
        Assert.Equal(3, configured.GetProperty("statistics").GetProperty("documentsAnalyzed").GetInt32());
        await AssertMatchesFull(configured);
        Assert.Equal(0, (await RunCliAsync("daemon", "stop")).ExitCode);
        Assert.Equal(0, (await RunCliAsync("daemon", "start")).ExitCode);
        await AssertMatchesFull(await CheckWithStats());
    }

    [Fact]
    public async Task Concurrent_auto_start_uses_one_daemon_and_recovers_after_crash()
    {
        File.WriteAllText(Path.Combine(_workDir, "A.cs"), "class Good {}");
        var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RunCliAsync("check", "--stats", "--format", "json")));
        foreach (var result in starts) Assert.True(result.ExitCode == 0, result.Stdout + result.Stderr);
        var analyzed = starts.Sum(result => JsonDocument.Parse(result.Stdout).RootElement.GetProperty("statistics").GetProperty("documentsAnalyzed").GetInt32());
        Assert.Equal(1, analyzed);
        var status = await RunCliAsync("daemon", "status");
        var pid = int.Parse(Regex.Match(status.Stdout, @"PID (\d+)").Groups[1].Value);
        using (var process = Process.GetProcessById(pid))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        var recovered = await CheckWithStats();
        Assert.Equal(1, recovered.GetProperty("statistics").GetProperty("documentsAnalyzed").GetInt32());
        var nextStatus = await RunCliAsync("daemon", "status");
        Assert.DoesNotContain($"PID {pid})", nextStatus.Stdout);
    }

    [Fact]
    public async Task Explicit_start_idle_shutdown_and_no_daemon_mode_have_observable_lifecycle()
    {
        var independent = await RunCliAsync("check", "--no-daemon", "--stats", "--format", "json");
        Assert.Equal(0, independent.ExitCode);
        using var json = JsonDocument.Parse(independent.Stdout);
        Assert.False(json.RootElement.GetProperty("statistics").GetProperty("daemonConnected").GetBoolean());
        Assert.Contains("stopped", (await RunCliAsync("daemon", "status")).Stdout);
        Assert.Equal(0, (await RunCliAsync("daemon", "start", "--idle-timeout", "0.3")).ExitCode);
        await Task.Delay(1000);
        Assert.Contains("stopped", (await RunCliAsync("daemon", "status")).Stdout);
        Assert.Equal(0, (await RunCliAsync("daemon", "stop")).ExitCode);
        Assert.Equal(2, (await RunCliAsync("daemon", "start", "--idle-timeout", "0")).ExitCode);
    }

    private async Task<JsonElement> CheckWithStats()
    {
        var result = await RunCliAsync("check", "--stats", "--format", "json");
        Assert.True(result.ExitCode is 0 or 1, result.Stdout + result.Stderr);
        using var document = JsonDocument.Parse(result.Stdout);
        Assert.True(document.RootElement.GetProperty("statistics").GetProperty("validated").GetBoolean());
        return document.RootElement.Clone();
    }

    private async Task AssertMatchesFull(JsonElement incremental)
    {
        var result = await RunCliAsync("check", "--no-daemon", "--format", "json");
        Assert.True(result.ExitCode is 0 or 1, result.Stdout + result.Stderr);
        using var full = JsonDocument.Parse(result.Stdout);
        Assert.Equal(full.RootElement.GetProperty("diagnostics").GetRawText(), incremental.GetProperty("diagnostics").GetRawText());
        Assert.Equal(full.RootElement.GetProperty("filesAnalyzed").GetInt32(), incremental.GetProperty("filesAnalyzed").GetInt32());
    }
}
