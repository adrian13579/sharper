using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SharpLint.Tests.Lsp;

public sealed class AgentWorkflowTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-agent-" + Guid.NewGuid().ToString("N"));
    private readonly List<McpClient> _clients = [];
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(45));
    private static string Server => Path.Combine(ServiceTests.RepositoryRoot(), "tests/SharpLint.FakeServer/bin/Debug/net10.0/SharpLint.FakeServer.dll");

    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        foreach (var client in _clients) await client.DisposeAsync();
        Directory.Delete(_root, true);
        _deadline.Dispose();
    }

    private string Repository(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, "Sample.cs"), "missing");
        return path;
    }

    private async Task<McpClient> Client(McpClientOptions? options = null, params string[] arguments)
    {
        var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet", WorkingDirectory = _root,
            Arguments = [RoslynFixture.CliPath, "mcp", "--server", Server, .. arguments],
        }), options, cancellationToken: _deadline.Token);
        _clients.Add(client);
        return client;
    }

    private async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?>? args = null)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: _deadline.Token);
        Assert.False(result.IsError == true, string.Join("; ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent.Value;
    }

    [Fact]
    public async Task One_global_server_checks_multiple_repositories_and_keeps_each_session_warm()
    {
        var a = Repository("one");
        var b = Repository("two");
        var client = await Client();
        var tools = await client.ListToolsAsync();
        var checkSchema = tools.Single(t => t.Name == "sharplint_check").JsonSchema;
        Assert.True(checkSchema.GetProperty("properties").TryGetProperty("workspace", out _));
        Assert.False(checkSchema.GetProperty("properties").TryGetProperty("server", out _));
        foreach (var root in new[] { a, b })
        {
            var check = await Call(client, "sharplint_check", new() { ["workspace"] = root });
            Assert.True(check.GetProperty("complete").GetBoolean());
            Assert.Equal("CS0103", check.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }
        var first = await Call(client, "sharplint_status", new() { ["workspace"] = a });
        var second = await Call(client, "sharplint_status", new() { ["workspace"] = b });
        Assert.NotEqual(first.GetProperty("processId").GetInt32(), second.GetProperty("processId").GetInt32());
        File.WriteAllText(Path.Combine(a, "Sample.cs"), "fixed");
        var fixedResult = await Call(client, "sharplint_check", new() { ["workspace"] = a });
        Assert.Equal(0, fixedResult.GetProperty("diagnosticCount").GetInt32());
        var again = await Call(client, "sharplint_status", new() { ["workspace"] = a });
        Assert.Equal(first.GetProperty("processId").GetInt32(), again.GetProperty("processId").GetInt32());
        Assert.Equal(1, again.GetProperty("sessionStarts").GetInt32());
    }

    [Fact]
    public async Task Absolute_source_path_discovers_repository_and_selects_only_the_file()
    {
        var root = Repository("repo");
        var nested = Path.Combine(root, "src");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "Nested.cs");
        File.WriteAllText(file, "fixed");
        var client = await Client();
        var result = await Call(client, "sharplint_check", new() { ["path"] = file });
        Assert.True(result.GetProperty("complete").GetBoolean());
        Assert.Equal(file, result.GetProperty("requestedPath").GetString());
        Assert.Equal(0, result.GetProperty("diagnosticCount").GetInt32());
        var status = await Call(client, "sharplint_status", new() { ["workspace"] = root });
        Assert.Equal(1, status.GetProperty("sessionStarts").GetInt32());
    }

    [Fact]
    public async Task Global_server_requires_repository_context_instead_of_scanning_its_launch_directory()
    {
        var client = await Client();
        var result = await client.CallToolAsync("sharplint_check");
        Assert.True(result.IsError);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), c => c.Text.Contains("absolute", StringComparison.Ordinal));
        var relative = await client.CallToolAsync("sharplint_check", new Dictionary<string, object?> { ["workspace"] = "relative" });
        Assert.True(relative.IsError);
    }

    [Fact]
    public async Task Fallback_workspace_remains_compatible_and_can_be_overridden_per_tool_call()
    {
        var a = Repository("fallback");
        var b = Repository("selected");
        var client = await Client(null, "--workspace", a);
        Assert.True((await Call(client, "sharplint_check")).GetProperty("complete").GetBoolean());
        Assert.Equal(b, (await Call(client, "sharplint_status", new() { ["workspace"] = b })).GetProperty("workspace").GetString());
        Assert.True((await Call(client, "sharplint_check", new() { ["path"] = Path.Combine(b, "Sample.cs") })).GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task LRU_pool_evicts_the_oldest_repository_and_closes_its_Roslyn_process()
    {
        var client = await Client();
        var roots = Enumerable.Range(0, 5).Select(i => Repository("repo" + i)).ToArray();
        var first = await Call(client, "sharplint_status", new() { ["workspace"] = roots[0] });
        var oldId = first.GetProperty("processId").GetInt32();
        foreach (var root in roots[1..]) await Call(client, "sharplint_status", new() { ["workspace"] = root });
        var restarted = await Call(client, "sharplint_status", new() { ["workspace"] = roots[0] });
        Assert.NotEqual(oldId, restarted.GetProperty("processId").GetInt32());
        Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(oldId));
    }

    [Fact]
    public async Task Per_call_solution_selects_a_separate_context_without_restarting_other_contexts()
    {
        var root = Repository("solutions");
        File.WriteAllText(Path.Combine(root, "One.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "Two.slnx"), "<Solution />");
        var client = await Client();
        var one = await Call(client, "sharplint_status", new() { ["workspace"] = root, ["solution"] = "One.slnx" });
        var two = await Call(client, "sharplint_status", new() { ["workspace"] = root, ["solution"] = "Two.slnx" });
        Assert.NotEqual(one.GetProperty("processId").GetInt32(), two.GetProperty("processId").GetInt32());
        var again = await Call(client, "sharplint_status", new() { ["workspace"] = root, ["solution"] = "One.slnx" });
        Assert.Equal(one.GetProperty("processId").GetInt32(), again.GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task Worktree_markers_and_solution_fallback_resolve_the_repository_boundary()
    {
        var worktree = Repository("worktree");
        Directory.Delete(Path.Combine(worktree, ".git"));
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: /external/metadata");
        var nested = Path.Combine(worktree, "src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Nested.cs"), "fixed");
        var loose = Path.Combine(_root, "loose");
        Directory.CreateDirectory(Path.Combine(loose, "src"));
        File.WriteAllText(Path.Combine(loose, "Example.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(loose, "src", "Example.csproj"), "project");
        File.WriteAllText(Path.Combine(loose, "src", "Sample.cs"), "fixed");
        var client = await Client();
        await Call(client, "sharplint_check", new() { ["path"] = Path.Combine(nested, "Nested.cs") });
        Assert.Equal(1, (await Call(client, "sharplint_status", new() { ["workspace"] = worktree })).GetProperty("sessionStarts").GetInt32());
        await Call(client, "sharplint_check", new() { ["path"] = Path.Combine(loose, "src", "Sample.cs") });
        Assert.Equal(1, (await Call(client, "sharplint_status", new() { ["workspace"] = loose })).GetProperty("sessionStarts").GetInt32());
    }

    [Fact]
    public async Task Invalid_solution_is_rejected_without_switching_the_selected_repository()
    {
        var a = Repository("a");
        var b = Repository("b");
        var solution = Path.Combine(b, "Other.slnx");
        File.WriteAllText(solution, "<Solution />");
        var client = await Client();
        var result = await client.CallToolAsync("sharplint_status", new Dictionary<string, object?> { ["workspace"] = a, ["solution"] = solution });
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Check_timeout_is_explicit_and_the_next_call_can_start_a_clean_session()
    {
        var root = Repository("timeout");
        File.WriteAllText(Path.Combine(root, "mode.txt"), "hang-check");
        var client = await Client(null, "--timeout", "1.5");
        var before = await Call(client, "sharplint_status", new() { ["workspace"] = root });
        var result = await Call(client, "sharplint_check", new() { ["workspace"] = root });
        Assert.Equal("timedOut", result.GetProperty("status").GetString());
        Assert.False(result.GetProperty("complete").GetBoolean());
        var after = await Call(client, "sharplint_status", new() { ["workspace"] = root });
        Assert.NotEqual(before.GetProperty("processId").GetInt32(), after.GetProperty("processId").GetInt32());
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Cancellation_releases_the_session_gate_and_disposes_the_uncertain_Roslyn_process(string protocol)
    {
        var root = Repository("cancel");
        File.WriteAllText(Path.Combine(root, "mode.txt"), "hang-check");
        var client = await Client(new McpClientOptions { ProtocolVersion = protocol });
        var before = await Call(client, "sharplint_status", new() { ["workspace"] = root });
        using var cancel = new CancellationTokenSource();
        var id = new RequestId("cancel-check");
        var pending = client.SendRequestAsync(new JsonRpcRequest
        {
            Id = id, Method = "tools/call",
            Params = JsonSerializer.SerializeToNode(new { name = "sharplint_check", arguments = new { workspace = root } }),
        }, cancel.Token);
        await Task.Delay(100);
        // Exercise the server-side protocol signal, independently of client-side wait cancellation.
        await client.SendNotificationAsync("notifications/cancelled", new CancelledNotificationParams { RequestId = id }, cancellationToken: _deadline.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        var after = await Call(client, "sharplint_status", new() { ["workspace"] = root });
        Assert.NotEqual(before.GetProperty("processId").GetInt32(), after.GetProperty("processId").GetInt32());
    }

#pragma warning disable MCP9005 // Exercise compatibility with older MCP clients advertising roots.
    [Fact]
    public async Task Advertised_client_roots_follow_repository_changes_and_reject_ambiguity()
    {
        var a = Repository("rootA");
        var b = Repository("rootB");
        string[] current = [a];
        var client = await Client(new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Roots = new RootsCapability() },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (_, _) => ValueTask.FromResult(new ListRootsResult { Roots = current.Select(p => new Root { Uri = new Uri(p).AbsoluteUri }).ToArray() }),
            },
        }, "--workspace", b);
        Assert.Equal(a, (await Call(client, "sharplint_status")).GetProperty("workspace").GetString());
        current = [b];
        Assert.Equal(b, (await Call(client, "sharplint_status")).GetProperty("workspace").GetString());
        current = [a, b];
        Assert.True((await client.CallToolAsync("sharplint_check")).IsError);
        Assert.True((await Call(client, "sharplint_check", new() { ["path"] = Path.Combine(a, "Sample.cs") })).GetProperty("complete").GetBoolean());
    }
#pragma warning restore MCP9005
}
