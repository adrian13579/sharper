using System.Text.Json;

namespace SharpLint.Analysis.Lsp;

public sealed class ServerCapabilities
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Method, JsonElement Options)> _registrations = new(StringComparer.Ordinal);
    private bool _staticDocumentDiagnostics;
    private bool _staticWorkspaceDiagnostics;
    private long _diagnosticRevision;

    public int SyncKind { get; private set; }
    public bool OpenClose { get; private set; }
    public long DiagnosticRevision => Interlocked.Read(ref _diagnosticRevision);
    public bool DocumentDiagnostics { get { lock (_gate) return _staticDocumentDiagnostics || _registrations.Values.Any(r => r.Method == "textDocument/diagnostic" && IsCSharp(r.Options)); } }
    public bool WorkspaceDiagnostics { get { lock (_gate) return _staticWorkspaceDiagnostics || _registrations.Values.Any(r => r.Method == "textDocument/diagnostic" && IsCSharp(r.Options) && IsTrue(r.Options, "workspaceDiagnostics")); } }
    public bool WatchedFiles { get { lock (_gate) return _registrations.Values.Any(r => r.Method == "workspace/didChangeWatchedFiles"); } }
    public IReadOnlyList<string> Providers
    {
        get
        {
            lock (_gate) return _registrations.Values.Where(r => r.Method == "textDocument/diagnostic" && IsCSharp(r.Options))
                .Select(r => r.Options.TryGetProperty("identifier", out var id) ? id.GetString() ?? "default" : "default")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
    }

    public void Initialize(JsonElement capabilities)
    {
        if (capabilities.TryGetProperty("positionEncoding", out var encoding) && encoding.GetString() != "utf-16")
            throw new NotSupportedException("Only UTF-16 LSP positions are supported.");
        if (capabilities.TryGetProperty("textDocumentSync", out var sync))
        {
            SyncKind = sync.ValueKind == JsonValueKind.Number ? sync.GetInt32() : sync.TryGetProperty("change", out var change) ? change.GetInt32() : 0;
            OpenClose = sync.ValueKind == JsonValueKind.Number ? SyncKind != 0 : IsTrue(sync, "openClose");
        }
        if (capabilities.TryGetProperty("diagnosticProvider", out var provider) && provider.ValueKind is JsonValueKind.Object or JsonValueKind.True)
        {
            _staticDocumentDiagnostics = true;
            _staticWorkspaceDiagnostics = provider.ValueKind == JsonValueKind.Object && IsTrue(provider, "workspaceDiagnostics");
        }
    }

    public void Register(JsonElement parameters)
    {
        lock (_gate)
            foreach (var registration in parameters.GetProperty("registrations").EnumerateArray())
            {
                _registrations[registration.GetProperty("id").GetString()!] = (registration.GetProperty("method").GetString()!,
                    registration.TryGetProperty("registerOptions", out var options) ? options.Clone() : JsonSerializer.SerializeToElement(new { }));
                if (registration.GetProperty("method").GetString() == "textDocument/diagnostic") Interlocked.Increment(ref _diagnosticRevision);
            }
    }

    public void Unregister(JsonElement parameters)
    {
        // "unregisterations" is the spelling used in the LSP 3.17 wire contract.
        if (!parameters.TryGetProperty("unregisterations", out var registrations)) registrations = parameters.GetProperty("unregistrations");
        lock (_gate)
            foreach (var registration in registrations.EnumerateArray())
                if (_registrations.Remove(registration.GetProperty("id").GetString()!, out var removed) && removed.Method == "textDocument/diagnostic")
                    Interlocked.Increment(ref _diagnosticRevision);
    }

    private static bool IsTrue(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool IsCSharp(JsonElement options)
        => !options.TryGetProperty("documentSelector", out var selector) || selector.ValueKind == JsonValueKind.Null
            || selector.EnumerateArray().Any(s => !s.TryGetProperty("language", out var language) || language.GetString() == "csharp");
}
