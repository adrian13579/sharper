using System.Collections.Concurrent;
using System.Text.Json;

namespace SharpLint.Analysis.Lsp;

public sealed class LspException(int code, string message) : Exception($"LSP {code}: {message}")
{
    public int Code { get; } = code;
}

/// <summary>One bounded stdio JSON-RPC connection. The reader never waits for an outbound request.</summary>
public sealed class LspConnection : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Task _reader;
    private long _nextId;
    private volatile Exception? _failure;

    public LspConnection(Stream input, Stream output, Func<string, JsonElement, ValueTask<object?>> handler)
    {
        _input = input;
        _output = output;
        _reader = ReadAsync(handler);
    }

    public Task Completion => _reader;
    public Exception? Failure => _failure;

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_failure is { } failure) throw new IOException("Language-server connection failed.", failure);
        var id = Interlocked.Increment(ref _nextId);
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var source = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = source;
        try
        {
            // Close the race with reader termination before/after insertion.
            if (_failure is { } terminated) throw new IOException("Language-server connection failed.", terminated);
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
            try { return await source.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // A cancellation frame must not itself wait indefinitely on a broken transport.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await NotifyAsync("$/cancelRequest", new { id }, timeout.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                throw;
            }
        }
        finally { _pending.TryRemove(key, out _); }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken = default)
        => SendAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        // JSON-RPC requires an explicit result:null for successful void requests.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await LspFraming.WriteAsync(_output, bytes, cancellationToken).ConfigureAwait(false); }
        finally { _writeGate.Release(); }
    }

    private async Task ReadAsync(Func<string, JsonElement, ValueTask<object?>> handler)
    {
        try
        {
            while (await LspFraming.ReadAsync(_input, _lifetime.Token).ConfigureAwait(false) is { } body)
            {
                using var document = JsonDocument.Parse(body);
                var message = document.RootElement;
                if (message.GetProperty("jsonrpc").GetString() != "2.0") throw new InvalidDataException("Expected JSON-RPC 2.0.");
                var hasId = message.TryGetProperty("id", out var id);
                if (message.TryGetProperty("method", out var method))
                {
                    var parameters = message.TryGetProperty("params", out var value) ? value.Clone() : default;
                    try
                    {
                        var result = await handler(method.GetString()!, parameters).ConfigureAwait(false);
                        if (hasId) await SendAsync(new { jsonrpc = "2.0", id = id.Clone(), result }, _lifetime.Token).ConfigureAwait(false);
                    }
                    catch (LspException ex) when (hasId)
                    {
                        await SendAsync(new { jsonrpc = "2.0", id = id.Clone(), error = new { code = ex.Code, message = ex.Message } }, _lifetime.Token).ConfigureAwait(false);
                    }
                    catch (LspException) when (!hasId) { } // Unknown notifications are extensible and have no response.
                }
                else if (hasId)
                {
                    if (!message.TryGetProperty("result", out var result) && !message.TryGetProperty("error", out _))
                        throw new InvalidDataException("LSP response has neither result nor error.");
                    // Parse before removing the source so malformed errors also fail the waiting caller.
                    var exception = message.TryGetProperty("error", out var error)
                        ? new LspException(error.GetProperty("code").GetInt32(), error.GetProperty("message").GetString() ?? "Unknown server error") : null;
                    if (!_pending.TryRemove(id.GetRawText(), out var pending)) continue; // Canceled/late response.
                    if (exception is not null) pending.TrySetException(exception);
                    else pending.TrySetResult(result.Clone());
                }
                else throw new InvalidDataException("Unrecognized JSON-RPC message.");
            }
            throw new EndOfStreamException("Roslyn language server disconnected or terminated.");
        }
        catch (Exception ex)
        {
            _failure = ex;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("Language-server connection ended: " + ex.Message, ex));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _reader.ConfigureAwait(false);
        _lifetime.Dispose();
        _writeGate.Dispose();
    }
}
