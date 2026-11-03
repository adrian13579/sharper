using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SharpLint.Analysis;
using SharpLint.Analysis.Lsp;

namespace SharpLint.Tests.Lsp;

public sealed class ProtocolTests
{
    [Fact]
    public async Task Framing_counts_utf8_bytes_and_reads_multiple_messages()
    {
        using var stream = new MemoryStream();
        var first = Encoding.UTF8.GetBytes("{\"text\":\"é😀\"}");
        await LspFraming.WriteAsync(stream, first);
        await LspFraming.WriteAsync(stream, "{}"u8.ToArray());
        Assert.StartsWith($"Content-Length: {first.Length}\r\n\r\n", Encoding.UTF8.GetString(stream.ToArray()));
        stream.Position = 0;
        Assert.Equal(first, await LspFraming.ReadAsync(stream));
        Assert.Equal("{}"u8.ToArray(), await LspFraming.ReadAsync(stream));
        Assert.Null(await LspFraming.ReadAsync(stream));
    }

    [Theory]
    [InlineData("Content-Length: -1\r\n\r\n")]
    [InlineData("Content-Length: 0\r\n\r\n")]
    [InlineData("Content-Length: 33554433\r\n\r\n")]
    [InlineData("Content-Type: application/json\r\n\r\n")]
    [InlineData("Content-Length: x\r\n\r\n")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("broken header\r\n\r\n")]
    public async Task Invalid_headers_fail_explicitly(string header)
        => await Assert.ThrowsAsync<InvalidDataException>(() => LspFraming.ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes(header))));

    [Theory]
    [InlineData("Content-Length: 2\r\n\r\n{")]
    [InlineData("Content-Length: 2\r\n")]
    public async Task Truncated_messages_fail(string message)
        => await Assert.ThrowsAsync<EndOfStreamException>(() => LspFraming.ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes(message))));

    [Fact]
    public async Task Oversized_header_and_cancelled_read_are_bounded()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => LspFraming.ReadAsync(new MemoryStream(new byte[8193])));
        using var stream = new ByteStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LspFraming.ReadAsync(stream, cancellation.Token));
    }

    [Fact]
    public async Task Framing_accepts_case_insensitive_headers_and_ignores_content_type()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Type: application/vscode-jsonrpc; charset=utf-8\r\ncontent-length: 2\r\n\r\n{}"));
        Assert.Equal("{}"u8.ToArray(), await LspFraming.ReadAsync(stream));
        await Assert.ThrowsAsync<InvalidDataException>(() => LspFraming.WriteAsync(new MemoryStream(), ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public async Task Framing_accepts_a_header_at_the_documented_size_limit()
    {
        const string suffix = "\r\nContent-Length: 2\r\n\r\n";
        var header = "X-Filler:" + new string('x', 8192 - "X-Filler:".Length - suffix.Length) + suffix;
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(header + "{}"));
        Assert.Equal("{}"u8.ToArray(), await LspFraming.ReadAsync(stream));
    }

    [Fact]
    public async Task Correlation_handles_out_of_order_responses_and_server_requests()
    {
        using var input = new ByteStream();
        using var output = new ByteStream();
        await using var connection = new LspConnection(input, output, (method, _) => ValueTask.FromResult<object?>(method == "ping" ? null : throw new LspException(-32601, "Unknown")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = connection.RequestAsync("first", new { text = "é" }, timeout.Token);
        var second = connection.RequestAsync("second", null, timeout.Token);
        var request1 = await Read(output, timeout.Token);
        var request2 = await Read(output, timeout.Token);
        await Send(input, new { jsonrpc = "2.0", id = request2.GetProperty("id").GetInt32(), result = "two" });
        await Send(input, new { jsonrpc = "2.0", id = "server-id", method = "ping" });
        var response = await Read(output, timeout.Token);
        Assert.Equal("server-id", response.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
        await Send(input, new { jsonrpc = "2.0", id = request1.GetProperty("id").GetInt32(), result = "one" });
        Assert.Equal("one", (await first).GetString());
        Assert.Equal("two", (await second).GetString());
        await Send(input, new { jsonrpc = "2.0", id = 99, method = "unknown" });
        Assert.Equal(-32601, (await Read(output, timeout.Token)).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Cancellation_sends_cancel_and_ignores_a_late_response()
    {
        using var input = new ByteStream();
        using var output = new ByteStream();
        await using var connection = new LspConnection(input, output, (_, _) => ValueTask.FromResult<object?>(null));
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var task = connection.RequestAsync("slow", null, cancellation.Token);
        var request = await Read(output, timeout.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var cancel = await Read(output, timeout.Token);
        Assert.Equal("$/cancelRequest", cancel.GetProperty("method").GetString());
        Assert.Equal(request.GetProperty("id").GetInt32(), cancel.GetProperty("params").GetProperty("id").GetInt32());
        await Send(input, new { jsonrpc = "2.0", id = request.GetProperty("id").GetInt32(), result = "late" });
        var next = connection.RequestAsync("next", null, timeout.Token);
        var nextRequest = await Read(output, timeout.Token);
        await Send(input, new { jsonrpc = "2.0", id = nextRequest.GetProperty("id").GetInt32(), result = true });
        Assert.True((await next).GetBoolean());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"jsonrpc\":\"1.0\",\"id\":1,\"result\":true}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{}}")]
    public async Task Malformed_responses_fail_pending_requests(string body)
    {
        using var input = new ByteStream();
        using var output = new ByteStream();
        await using var connection = new LspConnection(input, output, (_, _) => ValueTask.FromResult<object?>(null));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var task = connection.RequestAsync("test", null, timeout.Token);
        await Read(output, timeout.Token);
        await LspFraming.WriteAsync(input, Encoding.UTF8.GetBytes(body));
        await Assert.ThrowsAsync<IOException>(() => task);
        Assert.NotNull(connection.Failure);
    }

    [Fact]
    public async Task Eof_and_server_errors_are_not_empty_successes()
    {
        using var input = new ByteStream();
        using var output = new ByteStream();
        await using var connection = new LspConnection(input, output, (_, _) => ValueTask.FromResult<object?>(null));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failed = connection.RequestAsync("test", null, timeout.Token);
        var request = await Read(output, timeout.Token);
        await Send(input, new { jsonrpc = "2.0", id = request.GetProperty("id").GetInt32(), error = new { code = -32800, message = "Server cancelled" } });
        Assert.Equal(-32800, (await Assert.ThrowsAsync<LspException>(() => failed)).Code);
        var disconnected = connection.RequestAsync("test", null, timeout.Token);
        await Read(output, timeout.Token);
        input.Finish();
        await Assert.ThrowsAsync<IOException>(() => disconnected);
        await connection.Completion;
        await Assert.ThrowsAsync<IOException>(() => connection.RequestAsync("after-crash", null));
    }

    [Fact]
    public void Dynamic_registrations_respect_csharp_selectors_and_removal()
    {
        var capabilities = new ServerCapabilities();
        capabilities.Initialize(JsonSerializer.SerializeToElement(new { textDocumentSync = new { openClose = true, change = 2 } }));
        Assert.Equal(2, capabilities.SyncKind);
        Assert.True(capabilities.OpenClose);
        Assert.False(capabilities.DocumentDiagnostics);
        capabilities.Register(JsonSerializer.SerializeToElement(new { registrations = new[]
        {
            new { id = "razor", method = "textDocument/diagnostic", registerOptions = new { identifier = "razor", workspaceDiagnostics = false, documentSelector = new[] { new { language = "aspnetcorerazor" } } } },
            new { id = "csharp", method = "textDocument/diagnostic", registerOptions = new { identifier = "compiler", workspaceDiagnostics = true, documentSelector = new[] { new { language = "csharp" } } } },
        } }));
        Assert.True(capabilities.DocumentDiagnostics);
        Assert.True(capabilities.WorkspaceDiagnostics);
        Assert.Equal(["compiler"], capabilities.Providers);
        capabilities.Unregister(JsonSerializer.SerializeToElement(new { unregisterations = new[] { new { id = "csharp", method = "textDocument/diagnostic" } } }));
        Assert.False(capabilities.DocumentDiagnostics);
        Assert.False(capabilities.WorkspaceDiagnostics);
        Assert.Throws<NotSupportedException>(() => capabilities.Initialize(JsonSerializer.SerializeToElement(new { positionEncoding = "utf-8" })));
    }

    [Fact]
    public void Capability_changes_preserve_default_selectors_and_track_only_diagnostic_revisions()
    {
        var capabilities = new ServerCapabilities();
        capabilities.Initialize(JsonSerializer.SerializeToElement(new { positionEncoding = "utf-16" }));
        Assert.False(capabilities.OpenClose);
        Assert.Equal(0, capabilities.SyncKind);
        Assert.False(capabilities.WatchedFiles);
        capabilities.Register(JsonSerializer.SerializeToElement(new { registrations = new[] { new { id = "watch", method = "workspace/didChangeWatchedFiles" } } }));
        Assert.True(capabilities.WatchedFiles);
        Assert.Equal(0, capabilities.DiagnosticRevision);
        capabilities.Register(JsonSerializer.SerializeToElement(new { registrations = new object[]
        {
            new { id = "default", method = "textDocument/diagnostic" },
            new { id = "null-selector", method = "textDocument/diagnostic", registerOptions = new { identifier = (string?)null, documentSelector = (object?)null } },
        } }));
        Assert.True(capabilities.DocumentDiagnostics);
        Assert.Equal(new[] { "default" }, capabilities.Providers);
        var revision = capabilities.DiagnosticRevision;
        Assert.Equal(2, revision);
        capabilities.Unregister(JsonSerializer.SerializeToElement(new { unregistrations = new[] { new { id = "absent" }, new { id = "watch" } } }));
        Assert.False(capabilities.WatchedFiles);
        Assert.Equal(revision, capabilities.DiagnosticRevision);
        capabilities.Unregister(JsonSerializer.SerializeToElement(new { unregistrations = new[] { new { id = "default" }, new { id = "null-selector" } } }));
        Assert.False(capabilities.DocumentDiagnostics);
        Assert.Equal(revision + 2, capabilities.DiagnosticRevision);
    }

    private static async Task<JsonElement> Read(Stream stream, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse((await LspFraming.ReadAsync(stream, cancellationToken))!);
        return json.RootElement.Clone();
    }
    private static Task Send(Stream stream, object message) => LspFraming.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(message));

    private sealed class ByteStream : Stream
    {
        private readonly Channel<byte> _bytes = Channel.CreateUnbounded<byte>();
        public void Finish() => _bytes.Writer.TryComplete();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!await _bytes.Reader.WaitToReadAsync(cancellationToken)) return 0;
            var count = 0;
            while (count < buffer.Length && _bytes.Reader.TryRead(out var value)) buffer.Span[count++] = value;
            return count;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var value in buffer.Span) _bytes.Writer.TryWrite(value);
            return ValueTask.CompletedTask;
        }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
