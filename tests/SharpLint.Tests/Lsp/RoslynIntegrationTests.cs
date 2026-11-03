using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using SharpLint.Analysis;

namespace SharpLint.Tests.Lsp;

public sealed class RoslynFactAttribute : FactAttribute
{
    public RoslynFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHARPLINT_TEST_ROSLYN")))
            Skip = "Set SHARPLINT_TEST_ROSLYN to the official roslyn-language-server executable. See docs/lsp-service.md.";
    }
}

[Collection("Real Roslyn")]
public sealed class RoslynIntegrationTests
{
    [RoslynFact]
    public async Task Compiler_analyzer_custom_analyzer_fixes_external_edits_and_persistence()
    {
        await using var fixture = await RoslynFixture.CreateAsync();
        await using var service = fixture.Service();
        var first = await fixture.CheckAsync(service, "Domain/Widget.cs");
        Assert.Contains(first.Diagnostics, d => d.Code == "CS0103" && d.Severity == "error");
        Assert.Contains(first.Diagnostics, d => d.Code == "CA1822");
        Assert.Contains(first.Diagnostics, d => d.Code == "FIXTURE001" && d.Message.Contains("CustomTarget", StringComparison.Ordinal));
        Assert.Equal("verified", first.Freshness);
        var initial = await service.StatusAsync();
        File.WriteAllText(fixture.PathOf("Domain/Widget.cs"), File.ReadAllText(fixture.PathOf("Domain/Widget.cs")).Replace("missing", "42", StringComparison.Ordinal));
        var fixedResult = await fixture.CheckAsync(service, "Domain/Widget.cs");
        Assert.DoesNotContain(fixedResult.Diagnostics, d => d.Code == "CS0103");
        Assert.Contains(fixedResult.Diagnostics, d => d.Code == "FIXTURE001");
        File.WriteAllText(fixture.PathOf("Domain/Widget.cs"), File.ReadAllText(fixture.PathOf("Domain/Widget.cs")).Replace("=> 42", "=> unknown", StringComparison.Ordinal));
        Assert.Contains((await fixture.CheckAsync(service, "Domain/Widget.cs")).Diagnostics, d => d.Code == "CS0103");
        await fixture.CheckAsync(service, "Domain/Widget.cs");
        var final = await service.StatusAsync();
        Assert.Equal(initial.ProcessId, final.ProcessId);
        Assert.Equal(1, final.SessionStarts);
    }

    [RoslynFact]
    public async Task Semantic_dependencies_new_deleted_and_renamed_sources_are_reconciled()
    {
        await using var fixture = await RoslynFixture.CreateAsync();
        await using var service = fixture.Service();
        Assert.DoesNotContain((await fixture.CheckAsync(service, "Application/Consumer.cs")).Diagnostics, d => d.Severity == "error");
        var widget = fixture.PathOf("Domain/Widget.cs");
        File.WriteAllText(widget, File.ReadAllText(widget).Replace("int Value", "string Value", StringComparison.Ordinal).Replace("=> missing", "=> \"value\"", StringComparison.Ordinal));
        Assert.Contains((await fixture.CheckAsync(service, "Application/Consumer.cs")).Diagnostics, d => d.Code == "CS0029");
        File.WriteAllText(fixture.PathOf("Domain/New.cs"), "namespace Domain; public class New { int M() => missing; }");
        Assert.Contains((await fixture.CheckAsync(service)).Diagnostics, d => d.Code == "CS0103" && d.FilePath!.EndsWith("New.cs", StringComparison.Ordinal));
        File.Move(fixture.PathOf("Domain/New.cs"), fixture.PathOf("Domain/Renamed.cs"));
        Assert.Contains((await fixture.CheckAsync(service)).Diagnostics, d => d.Code == "CS0103" && d.FilePath!.EndsWith("Renamed.cs", StringComparison.Ordinal));
        File.Delete(fixture.PathOf("Domain/Renamed.cs"));
        Assert.DoesNotContain((await fixture.CheckAsync(service)).Diagnostics, d => d.FilePath!.EndsWith("Renamed.cs", StringComparison.Ordinal));
    }

    [RoslynFact]
    public async Task Project_reference_changes_reload_and_server_crashes_are_reported()
    {
        await using var fixture = await RoslynFixture.CreateAsync();
        await using var service = fixture.Service();
        await fixture.CheckAsync(service, "Application/Consumer.cs");
        var before = await service.StatusAsync();
        var project = fixture.PathOf("Application/Application.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace("<ProjectReference Include=\"../Domain/Domain.csproj\" />", "", StringComparison.Ordinal));
        var changed = await fixture.CheckAsync(service, "Application/Consumer.cs");
        Assert.Contains(changed.Diagnostics, d => d.Code is "CS0246" or "CS0234");
        var after = await service.StatusAsync();
        Assert.True(after.SessionStarts > before.SessionStarts);
        using var process = Process.GetProcessById(after.ProcessId!.Value);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        var failed = await service.CheckAsync("Domain/Widget.cs");
        Assert.Equal("failed", failed.Status);
        Assert.False(failed.Complete);
        Assert.Equal("uncertain", failed.Freshness);
        Assert.NotEmpty(failed.Errors);
        var recovered = await fixture.CheckAsync(service, "Domain/Widget.cs");
        Assert.Contains(recovered.Diagnostics, d => d.Code == "CS0103");
        Assert.True((await service.StatusAsync()).SessionStarts > after.SessionStarts);
    }

    [RoslynFact]
    public async Task Actual_mcp_stdio_entrypoint_returns_structured_diagnostics_and_metadata()
    {
        await using var fixture = await RoslynFixture.CreateAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet", WorkingDirectory = fixture.Root,
            Arguments = [RoslynFixture.CliPath, "mcp", "--server", RoslynFixture.ServerPath],
        }), cancellationToken: deadline.Token);
        var tools = await client.ListToolsAsync(cancellationToken: deadline.Token);
        Assert.Equal(new[] { "sharplint_check", "sharplint_status" }, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        var statusResult = await client.CallToolAsync("sharplint_status", new Dictionary<string, object?> { ["workspace"] = fixture.Root }, cancellationToken: deadline.Token);
        Assert.NotNull(statusResult.StructuredContent);
        Assert.True(statusResult.StructuredContent.Value.GetProperty("available").GetBoolean());
        JsonElement result = default;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var check = await client.CallToolAsync("sharplint_check", new Dictionary<string, object?> { ["workspace"] = fixture.Root, ["path"] = "Domain/Widget.cs" }, cancellationToken: deadline.Token);
            Assert.False(check.IsError == true);
            Assert.NotNull(check.StructuredContent);
            result = check.StructuredContent.Value;
            if (result.GetProperty("complete").GetBoolean()) break;
            await Task.Delay(150, deadline.Token);
        }
        Assert.True(result.GetProperty("complete").GetBoolean(), result.ToString());
        Assert.Equal("verified", result.GetProperty("freshness").GetString());
        Assert.Contains(result.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == "CS0103");
        Assert.True(result.GetProperty("diagnosticCount").GetInt32() > 0);
        Assert.True(result.GetProperty("durationMs").GetDouble() > 0);
        var statusAgain = await client.CallToolAsync("sharplint_status", new Dictionary<string, object?> { ["workspace"] = fixture.Root }, cancellationToken: deadline.Token);
        Assert.Equal(statusResult.StructuredContent.Value.GetProperty("processId").GetInt32(), statusAgain.StructuredContent!.Value.GetProperty("processId").GetInt32());
    }
}

[CollectionDefinition("Real Roslyn", DisableParallelization = true)]
public sealed class RealRoslynCollection;

internal sealed class RoslynFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sharplint-real-" + Guid.NewGuid().ToString("N"));
    public static string ServerPath => Environment.GetEnvironmentVariable("SHARPLINT_TEST_ROSLYN")!;
    public static string CliPath => Path.Combine(ServiceTests.RepositoryRoot(), "src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll");
    public string PathOf(string path) => Path.Combine(Root, path);
    public DiagnosticService Service() => new(new DiagnosticServiceOptions { RootPath = Root, ServerPath = ServerPath });

    public static async Task<RoslynFixture> CreateAsync()
    {
        var fixture = new RoslynFixture();
        Directory.CreateDirectory(fixture.Root);
        var source = Path.Combine(ServiceTests.RepositoryRoot(), "tests/SharpLint.Tests/Fixtures/Lsp/TestSolution");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")))
        {
            var target = fixture.PathOf(Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var restore = await RunAsync("dotnet", ["restore", "TestSolution.slnx", "-m:1", "--disable-build-servers"], fixture.Root);
        Assert.True(restore.Code == 0, restore.Output + restore.Error);
        var build = await RunAsync("dotnet", ["build", "CustomAnalyzer/CustomAnalyzer.csproj", "--no-restore", "-m:1", "--disable-build-servers"], fixture.Root);
        Assert.True(build.Code == 0, build.Output + build.Error);
        return fixture;
    }

    public async Task<DiagnosticResponse> CheckAsync(DiagnosticService service, string? path = null)
    {
        DiagnosticResponse? result = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            result = await service.CheckAsync(path);
            if (result.Complete) return result;
            if (result.Status != "incomplete") break;
            await Task.Delay(150);
        }
        Assert.True(result!.Complete, System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJson.Options));
        return result;
    }

    public static async Task<(int Code, string Output, string Error)> RunAsync(string command, IEnumerable<string> arguments, string directory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
}
