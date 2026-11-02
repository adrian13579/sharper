using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SharpLint.Analysis.Lsp;

internal sealed class RoslynSession : IAsyncDisposable
{
    private readonly DiagnosticServiceOptions _options;
    private readonly TaskCompletionSource _workspaceReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _stderr = new();
    private Process? _process;
    private Task? _stderrReader;
    private LspConnection? _connection;
    private string? _error;
    private long _refreshVersion;
    private readonly Dictionary<string, (int Version, IReadOnlyList<SharpLintDiagnostic> Diagnostics)> _published = new(StringComparer.Ordinal);

    public RoslynSession(DiagnosticServiceOptions options) => _options = options;
    public ServerCapabilities Capabilities { get; } = new();
    public string? ServerName { get; private set; }
    public string? LastError => _error;
    public long RefreshVersion => Interlocked.Read(ref _refreshVersion);
    public int? ProcessId { get; private set; }
    public bool Available => _process is { HasExited: false } && _connection?.Failure is null;
    public bool Ready => _workspaceReady.Task.IsCompletedSuccessfully && Available;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var executable = Discover(_options.ServerPath);
        var start = new ProcessStartInfo(executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : executable)
        {
            WorkingDirectory = _options.RootPath, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(executable);
        foreach (var argument in new[] { "--stdio", "--telemetryLevel", "off", "--logLevel", "Warning" }) start.ArgumentList.Add(argument);
        try
        {
            _process = Process.Start(start) ?? throw new IOException("Unable to start Roslyn language server.");
            ProcessId = _process.Id;
            _stderrReader = ReadStderrAsync(_process.StandardError);
            _connection = new LspConnection(_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream, HandleAsync);
            var rootUri = DiagnosticNormalizer.ToUri(_options.RootPath);
            var result = await RequestAsync("initialize", new
            {
                processId = Environment.ProcessId,
                clientInfo = new { name = "SharpLint", version = "0.2.0-alpha" },
                rootUri,
                workspaceFolders = new[] { new { uri = rootUri, name = Path.GetFileName(_options.RootPath) } },
                initializationOptions = new { autoLoadProjects = _options.SolutionPath is null ? 500 : 0 },
                capabilities = new
                {
                    general = new { positionEncodings = new[] { "utf-16" } },
                    workspace = new { configuration = true, diagnostics = new { refreshSupport = true },
                        didChangeWatchedFiles = new { dynamicRegistration = true, relativePatternSupport = true } },
                    window = new { workDoneProgress = true },
                    textDocument = new { diagnostic = new { dynamicRegistration = true, relatedDocumentSupport = false },
                        synchronization = new { dynamicRegistration = false }, publishDiagnostics = new { versionSupport = true, relatedInformation = true } },
                },
            }, cancellationToken).ConfigureAwait(false);
            Capabilities.Initialize(result.GetProperty("capabilities"));
            ServerName = result.TryGetProperty("serverInfo", out var info) ? info.GetProperty("name").GetString() : null;
            await NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
            if (_options.SolutionPath is not null)
                await NotifyAsync("solution/open", new { solution = DiagnosticNormalizer.ToUri(_options.SolutionPath) }, cancellationToken).ConfigureAwait(false);
            var finished = await Task.WhenAny(_workspaceReady.Task, _connection.Completion).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (finished != _workspaceReady.Task) throw new IOException("Roslyn disconnected before workspace initialization: " + _connection.Failure?.Message, _connection.Failure);
            await _workspaceReady.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IOException($"Roslyn startup failed: {ex.Message}. Check the server version, .NET runtime, SDK and restored project dependencies. {Stderr()}", ex);
        }
    }

    private ValueTask<object?> HandleAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "client/registerCapability": Capabilities.Register(parameters); break;
            case "client/unregisterCapability": Capabilities.Unregister(parameters); break;
            case "workspace/configuration":
                return ValueTask.FromResult<object?>(parameters.GetProperty("items").EnumerateArray().Select(item =>
                {
                    var section = item.GetProperty("section").GetString() ?? "";
                    // Analysis is read-only; restoring dependencies is the caller's responsibility.
                    if (section == "projects.dotnet_enable_automatic_restore") return (object?)false;
                    if (section.EndsWith("diagnostics_scope", StringComparison.Ordinal)) return "openFiles";
                    return null; // Preserve the server's defaults for settings we don't own.
                }).ToArray());
            case "workspace/workspaceFolders":
                return ValueTask.FromResult<object?>(new[] { new { uri = DiagnosticNormalizer.ToUri(_options.RootPath), name = Path.GetFileName(_options.RootPath) } });
            case "window/workDoneProgress/create": break;
            case "workspace/diagnostic/refresh": Interlocked.Increment(ref _refreshVersion); break;
            case "workspace/projectInitializationComplete": _workspaceReady.TrySetResult(); break;
            case "window/logMessage":
            case "window/showMessage":
            case "window/showMessageRequest":
                if (parameters.GetProperty("type").GetInt32() == 1) _error = parameters.GetProperty("message").GetString();
                break;
            case "textDocument/publishDiagnostics":
                // Unversioned pushes cannot establish freshness. Pull results are authoritative.
                if (parameters.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number)
                {
                    var uri = parameters.GetProperty("uri").GetString()!;
                    lock (_published)
                    {
                        if (!_published.TryGetValue(uri, out var previous) || previous.Version <= version.GetInt32())
                            _published[uri] = (version.GetInt32(), DiagnosticNormalizer.Parse(parameters.GetProperty("diagnostics"), uri));
                    }
                }
                break;
            case "workspace/applyEdit":
                return ValueTask.FromResult<object?>(new { applied = false, failureReason = "SharpLint is read-only." });
            case "$/progress":
            case "telemetry/event": break;
            default: throw new LspException(-32601, $"Unsupported client method: {method}");
        }
        return ValueTask.FromResult<object?>(null);
    }

    public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        => (_connection ?? throw new InvalidOperationException("Roslyn is not connected.")).RequestAsync(method, parameters, cancellationToken);

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        => (_connection ?? throw new InvalidOperationException("Roslyn is not connected.")).NotifyAsync(method, parameters, cancellationToken);

    private async Task ReadStderrAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) is var count && count > 0)
        {
            lock (_stderr)
            {
                _stderr.Append(buffer, 0, count);
                if (_stderr.Length > 8192) _stderr.Remove(0, _stderr.Length - 8192);
            }
        }
    }

    private string Stderr() { lock (_stderr) return _stderr.ToString().Trim(); }

    internal static string Discover(string? configured)
    {
        var name = configured ?? Environment.GetEnvironmentVariable("SHARPLINT_ROSLYN_PATH") ?? "roslyn-language-server";
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(name) ? Path.GetFullPath(name) : throw new FileNotFoundException($"Roslyn language server not found: {name}");
        var executableName = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"));
        foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException("Roslyn language server not found. Install roslyn-language-server or set --server / SHARPLINT_ROSLYN_PATH.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null && Available)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _connection.RequestAsync("shutdown", null, timeout.Token).ConfigureAwait(false);
                await _connection.NotifyAsync("exit", null, timeout.Token).ConfigureAwait(false);
                if (_process is not null) await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or LspException) { }
        }
        if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        if (_process is not null) await _process.WaitForExitAsync().ConfigureAwait(false);
        if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
        if (_stderrReader is not null) await _stderrReader.ConfigureAwait(false);
        _process?.Dispose();
    }
}
