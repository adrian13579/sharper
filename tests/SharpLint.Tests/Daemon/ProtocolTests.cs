using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SharpLint.Daemon;

namespace SharpLint.Tests.Daemon;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(Protocol.MaximumMessageBytes + 1)]
    public async Task Invalid_frame_lengths_are_rejected_before_payload_allocation(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync<DaemonRequest>(stream, default));
    }

    [Fact]
    public async Task Frames_round_trip_and_truncation_null_and_malformed_json_fail()
    {
        using var stream = new MemoryStream();
        var request = new DaemonRequest(1, "request", "Status", "/source", "build");
        await Protocol.WriteAsync(stream, request, default);
        stream.Position = 0;
        Assert.Equal(request, await Protocol.ReadAsync<DaemonRequest>(stream, default));
        using var truncated = new MemoryStream(stream.ToArray()[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => Protocol.ReadAsync<DaemonRequest>(truncated, default));
        foreach (var payload in new[] { "null", "{" })
        {
            using var invalid = new MemoryStream();
            var bytes = Encoding.UTF8.GetBytes(payload);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            invalid.Write(header);
            invalid.Write(bytes);
            invalid.Position = 0;
            await Assert.ThrowsAnyAsync<Exception>(() => Protocol.ReadAsync<DaemonRequest>(invalid, default));
        }
    }

    [Fact]
    public async Task Real_pipe_rejects_incompatible_wrong_repository_and_unknown_requests_without_killing_host()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharplint-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var identity = DaemonIdentity.Create(root);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var host = new DaemonHost().RunAsync(identity, TimeSpan.FromMinutes(1), cancellation.Token);
        try
        {
            foreach (var request in new[]
            {
                new DaemonRequest(999, "1", "Handshake", identity.Root, DaemonIdentity.BuildId),
                new DaemonRequest(1, "2", "Handshake", identity.Root, "old-build"),
                new DaemonRequest(1, "3", "Status", "/wrong-repository", DaemonIdentity.BuildId),
                new DaemonRequest(1, "4", "Unknown", identity.Root, DaemonIdentity.BuildId),
                new DaemonRequest(1, "", "Status", identity.Root, DaemonIdentity.BuildId),
            })
            {
                using var pipe = new NamedPipeClientStream(".", identity.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(cancellation.Token);
                await Protocol.WriteAsync(pipe, request, cancellation.Token);
                var response = await Protocol.ReadAsync<DaemonResponse>(pipe, cancellation.Token);
                Assert.Equal("error", response.Status);
                Assert.NotEmpty(response.Error!);
            }
            var status = await new DaemonClient(identity).SendAsync("Status", cancellationToken: cancellation.Token);
            Assert.Equal("completed", status.Status);
            Assert.Equal(Environment.ProcessId, status.ProcessId);
            // A partial/disconnected client does not prevent status requests.
            using (var disconnected = new NamedPipeClientStream(".", identity.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await disconnected.ConnectAsync(cancellation.Token);
                await disconnected.WriteAsync(new byte[] { 42 }, cancellation.Token);
            }
            Assert.Equal("completed", (await new DaemonClient(identity).SendAsync("Status", cancellationToken: cancellation.Token)).Status);
            await new DaemonClient(identity).SendAsync("Shutdown", cancellationToken: cancellation.Token);
            await host.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await cancellation.CancelAsync();
            await host;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Host_exits_when_its_analysis_root_is_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharplint-deleted-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var identity = DaemonIdentity.Create(root);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new DaemonHost().RunAsync(identity, TimeSpan.FromMinutes(1), cancellation.Token);
        try
        {
            Assert.Equal("completed", (await new DaemonClient(identity).SendAsync("Status", cancellationToken: cancellation.Token)).Status);
            Directory.Delete(root, true);
            await host.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(await new DaemonClient(identity).StatusAsync(cancellation.Token));
        }
        finally
        {
            await cancellation.CancelAsync();
            await host;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Identity_is_canonical_repository_specific_and_symlink_aware()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharplint-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            var identity = DaemonIdentity.Create(root);
            Assert.Equal(identity, DaemonIdentity.Create(Path.Combine(root, "nested", "..")));
            Assert.NotEqual(identity.PipeName, DaemonIdentity.Create(Path.Combine(root, "nested")).PipeName);
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(root, "nested"));
                Assert.Equal(DaemonIdentity.Create(Path.Combine(root, "nested")), DaemonIdentity.Create(Path.Combine(root, "link")));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
