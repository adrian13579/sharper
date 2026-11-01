using System.Buffers.Binary;
using System.Text.Json;
using SharpLint.Analysis;

namespace SharpLint.Daemon;

public sealed record DaemonRequest(int ProtocolVersion, string RequestId, string Method, string Root, string BuildId);

public sealed record DaemonResponse(int ProtocolVersion, string RequestId, string Status,
    string BuildId, int ProcessId, SnapshotResult? Analysis = null, string? Error = null);

/// <summary>One request/response per connection, four-byte little-endian length followed by UTF-8 JSON.</summary>
public static class Protocol
{
    public const int MaximumMessageBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, s_options);
        if (payload.Length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("IPC payload exceeds the message limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken,
        int maximumBytes = MaximumMessageBytes)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maximumBytes) throw new InvalidDataException("Invalid IPC message length.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, s_options) ?? throw new InvalidDataException("Null IPC payload.");
    }
}
