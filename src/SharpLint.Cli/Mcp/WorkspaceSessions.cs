using System.Diagnostics;
using SharpLint.Analysis;

namespace SharpLint.Cli.Mcp;

/// <summary>One bounded pool of lazy, persistent repository sessions per MCP server.</summary>
public sealed class WorkspaceSessions(DiagnosticServiceOptions defaults, string? defaultWorkspace) : IAsyncDisposable
{
    private const int Capacity = 4;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, Entry> _entries = new(WorkspaceResolver.PathComparer);
    private long _sequence;
    internal TimeSpan Timeout => defaults.Timeout;

    public async Task<DiagnosticResponse> CheckAsync(string? path, string? workspace, string? solution, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var settings = Settings(path, workspace, solution, roots);
        var requested = Path.GetFullPath(path ?? settings.RootPath, settings.RootPath);
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        var entered = false;
        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            var service = await GetAsync(settings).ConfigureAwait(false);
            var response = await service.CheckAsync(path, deadline.Token).ConfigureAwait(false);
            response = response with { DurationMs = clock.Elapsed.TotalMilliseconds };
            return !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested && response.Status == "cancelled"
                ? response with { Status = "timedOut", Errors = ["Diagnostic check timed out."] } : response;
        }
        catch (OperationCanceledException)
        {
            return new(cancellationToken.IsCancellationRequested ? "cancelled" : "timedOut", File.Exists(requested) ? "file" : "documents", requested,
                false, "uncertain", [], [cancellationToken.IsCancellationRequested ? "Diagnostic check cancelled." : "Diagnostic check timed out waiting for a session."],
                ["Scope is C# document pull diagnostics."], clock.Elapsed.TotalMilliseconds);
        }
        finally { if (entered) _gate.Release(); }
    }

    public async Task<ServiceStatus> StatusAsync(string? workspace, string? solution, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var settings = Settings(null, workspace, solution, roots);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try { return await (await GetAsync(settings).ConfigureAwait(false)).StatusAsync(deadline.Token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private DiagnosticServiceOptions Settings(string? path, string? workspace, string? solution, IReadOnlyList<string> roots)
    {
        var root = WorkspaceResolver.Resolve(workspace, path, defaultWorkspace, roots);
        var selected = solution ?? (defaultWorkspace is not null && WorkspaceResolver.PathComparer.Equals(root, defaultWorkspace) ? defaults.SolutionPath : null);
        var fullSolution = selected is null ? null : Path.GetFullPath(selected, root);
        if (fullSolution is not null && !WorkspaceResolver.Contains(root, fullSolution)) throw new ArgumentException("solution must be inside the selected repository.");
        if (fullSolution is not null && (!File.Exists(fullSolution) || Path.GetExtension(fullSolution) is not (".sln" or ".slnx")))
            throw new ArgumentException("solution must be an existing .sln or .slnx file.");
        return defaults with { RootPath = root, SolutionPath = fullSolution };
    }

    private async Task<DiagnosticService> GetAsync(DiagnosticServiceOptions settings)
    {
        var key = settings.RootPath + "\0" + settings.SolutionPath;
        if (!_entries.TryGetValue(key, out var entry))
        {
            if (_entries.Count == Capacity)
            {
                var oldest = _entries.MinBy(e => e.Value.Sequence);
                _entries.Remove(oldest.Key);
                await oldest.Value.Service.DisposeAsync().ConfigureAwait(false);
            }
            entry = new(new DiagnosticService(settings));
            _entries.Add(key, entry);
        }
        entry.Sequence = ++_sequence;
        return entry.Service;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var entry in _entries.Values) await entry.Service.DisposeAsync().ConfigureAwait(false);
            _entries.Clear();
        }
        finally { _gate.Release(); _gate.Dispose(); }
    }

    private sealed class Entry(DiagnosticService service)
    {
        internal DiagnosticService Service { get; } = service;
        internal long Sequence { get; set; }
    }
}
