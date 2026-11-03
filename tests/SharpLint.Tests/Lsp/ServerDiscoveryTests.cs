using SharpLint.Analysis;

namespace SharpLint.Tests.Lsp;

[CollectionDefinition("Server discovery environment", DisableParallelization = true)]
public sealed class ServerDiscoveryEnvironment;

[Collection("Server discovery environment")]
public sealed class ServerDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-discovery-" + Guid.NewGuid().ToString("N"));
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");
    private readonly string? _originalServer = Environment.GetEnvironmentVariable("SHARPLINT_ROSLYN_PATH");
    private static string Server => Path.Combine(ServiceTests.RepositoryRoot(), "tests/SharpLint.FakeServer/bin/Debug/net10.0/SharpLint.FakeServer.dll");

    public ServerDiscoveryTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("SHARPLINT_ROSLYN_PATH", _originalServer);
        Directory.Delete(_root, true);
    }

    private DiagnosticService Service(string? server) => new(new DiagnosticServiceOptions
    {
        RootPath = _root, ServerPath = server, Timeout = TimeSpan.FromSeconds(5),
    });

    [Fact]
    public async Task Configured_executable_takes_precedence_over_environment()
    {
        Environment.SetEnvironmentVariable("SHARPLINT_ROSLYN_PATH", Path.Combine(_root, "absent.dll"));
        await using var service = Service(Server);
        var status = await service.StatusAsync();
        Assert.True(status.Available, status.LastError);
        Assert.Equal("SharpLint controlled test server", status.ServerName);
    }

    [Fact]
    public async Task Environment_executable_name_is_discovered_on_path()
    {
        Environment.SetEnvironmentVariable("PATH", Path.PathSeparator + Path.GetDirectoryName(Server) + Path.PathSeparator + _originalPath);
        Environment.SetEnvironmentVariable("SHARPLINT_ROSLYN_PATH", Path.GetFileName(Server));
        await using var service = Service(null);
        var status = await service.StatusAsync();
        Assert.True(status.Available, status.LastError);
        Assert.True(status.DiagnosticsAvailable);
    }

    [Fact]
    public async Task Relative_configured_path_is_resolved_before_changing_process_working_directory()
    {
        await using var service = Service(Path.GetRelativePath(Directory.GetCurrentDirectory(), Server));
        var status = await service.StatusAsync();
        Assert.True(status.Available, status.LastError);
        Assert.Equal(_root, status.Workspace);
    }

    [Fact]
    public async Task Missing_named_executable_produces_actionable_discovery_error()
    {
        await using var service = Service("missing-sharplint-server-" + Guid.NewGuid().ToString("N"));
        var status = await service.StatusAsync();
        Assert.False(status.Available);
        Assert.False(status.DiagnosticsAvailable);
        Assert.Equal("failed", status.WorkspaceState);
        Assert.Contains("SHARPLINT_ROSLYN_PATH", status.LastError);
    }
}
