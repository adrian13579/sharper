using System.Diagnostics;
using System.Text.Json;
using SharpLint.Analysis.Lsp;

namespace SharpLint.Analysis;

/// <summary>A single serialized, persistent Roslyn session shared by all callers.</summary>
public sealed class DiagnosticService : IAsyncDisposable
{
    private static readonly string[] s_limitations =
    [
        "Scope is C# document pull diagnostics, not a complete solution build. Project-level, closed-document and non-local analyzer findings may be absent.",
        "Freshness covers reconciled files inside the workspace root. External imports, linked files, SDKs and NuGet caches must remain unchanged; restart after external changes.",
        "Only the active Roslyn project context is checked for linked or multi-targeted documents.",
    ];
    private readonly DiagnosticServiceOptions _options;
    private readonly FileSynchronization _files;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, DocumentState> _documents = new(FileSynchronization.PathComparer);
    private RoslynSession? _session;
    private DiskSnapshot? _synchronized;
    private string? _lastError;
    private int _sessionStarts;

    public DiagnosticService(DiagnosticServiceOptions options)
    {
        var root = Path.GetFullPath(options.RootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Workspace directory not found: {root}");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be between zero and one hour.");
        var solution = options.SolutionPath is null ? null : Path.GetFullPath(options.SolutionPath, root);
        if (solution is not null && (!File.Exists(solution) || Path.GetExtension(solution) is not (".sln" or ".slnx")))
            throw new ArgumentException("Solution must be an existing .sln or .slnx file.", nameof(options));
        _options = options with { RootPath = root, SolutionPath = solution };
        _files = new FileSynchronization(root);
    }

    public async Task<DiagnosticResponse> CheckAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var scope = "documents";
        var requested = path ?? _options.RootPath;
        var diagnostics = new List<SharpLintDiagnostic>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);
        var entered = false;
        try
        {
            requested = ResolvePath(path);
            scope = File.Exists(requested) ? "file" : "documents";
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            // The watcher is a hint, never the source of truth. Every request fully reconciles disk.
            var hints = _files.Drain();
            if (hints.Events.Count > 0) await Task.Delay(75, deadline.Token).ConfigureAwait(false);
            if (_session is not null && !_session.Available)
                throw new IOException("Roslyn language server terminated unexpectedly. Run a new check to start a fresh session.");
            // Roslyn's project system can generate inputs during startup. Establish the disk baseline
            // after its explicit readiness barrier, before opening/synchronizing documents.
            await EnsureSessionAsync(deadline.Token).ConfigureAwait(false);
            var epoch = _files.Epoch;
            var before = await _files.ReadAsync(deadline.Token).ConfigureAwait(false);
            if (_synchronized is not null && (before.RequiresReload(_synchronized) || hints.Overflowed))
            {
                await NotifyDiskChangesAsync(_session!, before, _synchronized, deadline.Token).ConfigureAwait(false);
                await CloseSessionAsync().ConfigureAwait(false);
                await EnsureSessionAsync(deadline.Token).ConfigureAwait(false);
                epoch = _files.Epoch;
                before = await _files.ReadAsync(deadline.Token).ConfigureAwait(false);
            }
            var selected = before.Files.Where(f => FileSynchronization.IsSource(f.Key) && InScope(f.Key, requested, scope))
                .Select(f => f.Key).Order(StringComparer.Ordinal).ToArray();
            if (selected.Length == 0) throw new IOException("No C# source files found in the requested scope (or the file was deleted).");
            var session = _session!;
            if (!session.Capabilities.DocumentDiagnostics)
                return Response("incomplete", false, "uncertain", ["Server does not advertise C# document pull diagnostics. Push-only results cannot establish freshness."]);
            if (!session.Capabilities.OpenClose || session.Capabilities.SyncKind is not (1 or 2))
                throw new NotSupportedException("Server does not support the required open/close and document synchronization capabilities.");

            if (_synchronized is not null) await NotifyDiskChangesAsync(session, before, _synchronized, deadline.Token).ConfigureAwait(false);
            // Synchronize every source in the boundary, including dependencies of a single requested file.
            foreach (var (file, input) in before.Files.Where(f => FileSynchronization.IsSource(f.Key)))
            {
                var uri = DiagnosticNormalizer.ToUri(file);
                if (!_documents.TryGetValue(file, out var state))
                {
                    state = new DocumentState(input.Text!);
                    await session.NotifyAsync("textDocument/didOpen", new { textDocument = new { uri, languageId = "csharp", version = state.Version, text = state.Text } }, deadline.Token).ConfigureAwait(false);
                    _documents[file] = state;
                }
                else if (state.Text != input.Text)
                {
                    var change = state.Change(input.Text!, session.Capabilities.SyncKind);
                    await session.NotifyAsync("textDocument/didChange", new { textDocument = new { uri, version = state.Version }, contentChanges = new[] { change } }, deadline.Token).ConfigureAwait(false);
                }
            }
            _synchronized = before;
            var recognized = true;
            var stableRefresh = false;
            var verifiedRefresh = session.RefreshVersion;
            var capabilityRevision = session.Capabilities.DiagnosticRevision;
            // Opening documents can trigger Roslyn's first diagnostic refresh. Repull at most once
            // within the same deadline; never bless the pre-refresh report or retry indefinitely.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var refresh = session.RefreshVersion;
                recognized = true;
                diagnostics.Clear();
                foreach (var file in selected)
                {
                    var state = _documents[file];
                    var uri = DiagnosticNormalizer.ToUri(file);
                    var report = await session.RequestAsync("textDocument/diagnostic", new
                    {
                        textDocument = new { uri }, previousResultId = state.ResultId,
                    }, deadline.Token).ConfigureAwait(false);
                    var kind = report.GetProperty("kind").GetString();
                    var resultId = report.TryGetProperty("resultId", out var id) ? id.GetString() : null;
                    if (kind == "full") state.Diagnostics = DiagnosticNormalizer.Parse(report.GetProperty("items"), uri);
                    else if (kind != "unchanged" || state.ResultId is null || state.ResultId != resultId)
                        throw new InvalidDataException("Invalid or uncached unchanged diagnostic report.");
                    state.ResultId = resultId;
                    recognized &= resultId is not null; // Roslyn returns no resultId for unrecognized documents.
                    diagnostics.AddRange(state.Diagnostics);
                }
                if (refresh == session.RefreshVersion) { stableRefresh = true; verifiedRefresh = refresh; break; }
            }
            var after = await _files.ReadAsync(deadline.Token).ConfigureAwait(false);
            if (!before.Matches(after) || epoch != _files.Epoch)
                return Response("incomplete", false, "uncertain", ["Workspace files changed during analysis. Run the check again."]);
            if (!recognized)
                return Response("incomplete", false, "uncertain", ["Roslyn did not confirm diagnostics for every requested document (missing resultId). Check solution membership and loading."]);
            if (session.LastError is { } error)
                return Response("incomplete", false, "uncertain", [error]);
            if (!session.Available)
                throw new IOException("Roslyn language server terminated during analysis.");
            if (!stableRefresh || verifiedRefresh != session.RefreshVersion)
                return Response("incomplete", false, "uncertain", ["Roslyn requested diagnostic refresh during analysis. Run the check again."]);
            if (capabilityRevision != session.Capabilities.DiagnosticRevision)
                return Response("incomplete", false, "uncertain", ["Diagnostic capabilities changed during analysis. Run the check again."]);
            _lastError = null;
            return Response("completed", true, "verified", []);
        }
        catch (OperationCanceledException)
        {
            // A partially delivered synchronization message cannot safely be reused.
            if (entered) await CloseSessionAsync().ConfigureAwait(false);
            return Response(cancellationToken.IsCancellationRequested ? "cancelled" : "timedOut", false, "uncertain",
                [cancellationToken.IsCancellationRequested ? "Diagnostic check cancelled." : "Diagnostic check timed out."]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException or FormatException or OverflowException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or LspException or JsonException or System.ComponentModel.Win32Exception)
        {
            if (entered) await CloseSessionAsync().ConfigureAwait(false);
            return Response("failed", false, "uncertain", [ex.Message]);
        }
        finally { if (entered) _gate.Release(); }

        DiagnosticResponse Response(string status, bool complete, string freshness, IReadOnlyList<string> errors)
        {
            if (errors.Count > 0) _lastError = string.Join("; ", errors);
            return new(status, scope, requested, complete, freshness, DiagnosticNormalizer.Order(diagnostics), errors, s_limitations, clock.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<ServiceStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);
        var entered = false;
        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            await EnsureSessionAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or ArgumentException or LspException or NotSupportedException or JsonException or System.ComponentModel.Win32Exception)
        {
            _lastError = ex is OperationCanceledException ? "Workspace initialization cancelled or timed out." : ex.Message;
            if (entered && _session?.Ready != true) await CloseSessionAsync().ConfigureAwait(false);
        }
        finally { if (entered) _gate.Release(); }
        return new(_session?.Available == true, _session?.Ready == true ? "ready" : "failed", _options.RootPath,
            _options.SolutionPath ?? FindSolution(), _session?.Capabilities.DocumentDiagnostics == true,
            _lastError ?? _session?.LastError, _sessionStarts, _session?.ProcessId, _session?.ServerName,
            _session?.Capabilities.Providers ?? [], _session?.Capabilities.WorkspaceDiagnostics == true, s_limitations);
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            if (!_session.Ready) throw new IOException("Roslyn session is unavailable. Restart SharpLint to recover.");
            return;
        }
        if (_options.SolutionPath is null && FindConfiguredSolution() is null && Directory.EnumerateFiles(_options.RootPath)
            .Count(p => Path.GetExtension(p) is ".sln" or ".slnx") > 1)
            throw new ArgumentException("Multiple solutions found. Select one with --solution.");
        _session = new RoslynSession(_options);
        _sessionStarts++;
        await _session.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task NotifyDiskChangesAsync(RoslynSession session, DiskSnapshot current, DiskSnapshot previous, CancellationToken cancellationToken)
    {
        if (!session.Capabilities.WatchedFiles) return;
        var changes = current.Files.Where(f => !previous.Files.TryGetValue(f.Key, out var old) || old.Fingerprint != f.Value.Fingerprint)
            .Select(f => new { uri = DiagnosticNormalizer.ToUri(f.Key), type = previous.Files.ContainsKey(f.Key) ? 2 : 1 })
            .Concat(previous.Files.Keys.Except(current.Files.Keys, FileSynchronization.PathComparer)
                .Select(f => new { uri = DiagnosticNormalizer.ToUri(f), type = 3 })).ToArray();
        if (changes.Length > 0) await session.NotifyAsync("workspace/didChangeWatchedFiles", new { changes }, cancellationToken).ConfigureAwait(false);
    }

    private string? FindSolution()
    {
        try
        {
            return FindConfiguredSolution() ?? (Directory.Exists(_options.RootPath)
                ? Directory.EnumerateFiles(_options.RootPath).Where(p => Path.GetExtension(p) is ".sln" or ".slnx").Order(StringComparer.Ordinal).FirstOrDefault() : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private string? FindConfiguredSolution()
    {
        // Reflect the documented Roslyn auto-load selection in status; evaluation still belongs to Roslyn.
        var settings = Path.Combine(_options.RootPath, ".vscode", "settings.json");
        if (!File.Exists(settings)) return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (json.RootElement.TryGetProperty("dotnet.defaultSolution", out var selected) && selected.ValueKind == JsonValueKind.String
                && selected.GetString() is { Length: > 0 } value && value != "disable")
            {
                var solution = Path.GetFullPath(value, _options.RootPath);
                if (File.Exists(solution)) return solution;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { }
        return null;
    }

    private string ResolvePath(string? path)
    {
        var resolved = Path.GetFullPath(path ?? _options.RootPath, _options.RootPath);
        if (!InScope(resolved, _options.RootPath, "documents")) throw new ArgumentException("Requested path must be inside the workspace root.");
        if (!Directory.Exists(resolved) && (!File.Exists(resolved) || !FileSynchronization.IsSource(resolved)))
            throw new ArgumentException("Requested path must be an existing C# file or directory.");
        return resolved;
    }

    private static bool InScope(string file, string requested, string scope)
        => FileSynchronization.PathComparer.Equals(file, requested) || scope != "file"
            && Path.GetRelativePath(requested, file) is var relative && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);

    private async Task CloseSessionAsync()
    {
        if (_session is not null)
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            if (_session.Available)
            {
                foreach (var file in _documents.Keys)
                {
                    try { await _session.NotifyAsync("textDocument/didClose", new { textDocument = new { uri = DiagnosticNormalizer.ToUri(file) } }, close.Token).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException) { break; }
                }
            }
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        _session = null;
        _synchronized = null;
        _documents.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _files.Dispose(); await CloseSessionAsync().ConfigureAwait(false); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
