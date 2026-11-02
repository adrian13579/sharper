using System.Globalization;
using System.Text;

namespace SharpLint.Analysis.Lsp;

public static class LspFraming
{
    private const int MaximumBodyLength = 32 * 1024 * 1024;

    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                if (header.Count == 0) return null;
                throw new EndOfStreamException("LSP header was truncated.");
            }
            header.Add(one[0]);
            if (header.Count > 8192) throw new InvalidDataException("LSP header exceeds 8192 bytes.");
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        int? length = null;
        foreach (var line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.Split(':', 2);
            if (pair.Length != 2) throw new InvalidDataException("Malformed LSP header.");
            if (!pair[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (length is not null || !int.TryParse(pair[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed <= 0 || parsed > MaximumBodyLength) throw new InvalidDataException("Invalid LSP Content-Length.");
            length = parsed;
        }
        if (length is null) throw new InvalidDataException("LSP Content-Length is missing.");
        var body = new byte[length.Value];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        if (body.Length is <= 0 or > MaximumBodyLength) throw new InvalidDataException("Invalid LSP body size.");
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n"), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
