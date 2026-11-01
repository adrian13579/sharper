using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SharpLint.Analysis;

namespace SharpLint.Daemon;

public sealed class DaemonHost
{
    public async Task RunAsync(DaemonIdentity identity, TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        if (idleTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        identity.EnsureStateDirectory();
        using var ownership = new FileStream(identity.LockPath("host"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var coordinator = new WorkspaceCoordinator(identity.Root);
        using var slots = new SemaphoreSlim(16, 16);
        var clients = new HashSet<Task>();
        var lastActivity = Stopwatch.GetTimestamp();
        var idle = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Clamp(idleTimeout.TotalMilliseconds, 1, 1000)));
                while (await timer.WaitForNextTickAsync(shutdown.Token).ConfigureAwait(false))
                {
                    lock (clients)
                    {
                        if (!Directory.Exists(identity.Root)
                            || (clients.Count == 0 && Stopwatch.GetElapsedTime(lastActivity) >= idleTimeout)) shutdown.Cancel();
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        }, CancellationToken.None);

        NamedPipeServerStream NewListener() => new(identity.PipeName, PipeDirection.InOut, 17,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var listener = NewListener();
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await slots.WaitAsync(shutdown.Token).ConfigureAwait(false);
                try { await listener.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false); }
                catch { slots.Release(); throw; }
                var pipe = listener;
                // Keep the Unix pipe listener alive across short connections, including malformed/disconnected clients.
                listener = NewListener();
                lock (clients) lastActivity = Stopwatch.GetTimestamp();
                var task = HandleAsync(pipe, identity, coordinator, shutdown);
                lock (clients) clients.Add(task);
                _ = task.ContinueWith(completed =>
                {
                    lock (clients)
                    {
                        clients.Remove(completed);
                        lastActivity = Stopwatch.GetTimestamp();
                    }
                    slots.Release();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            listener.Dispose();
            await shutdown.CancelAsync().ConfigureAwait(false);
            Task[] remaining;
            lock (clients) remaining = clients.ToArray();
            await Task.WhenAll(remaining).ConfigureAwait(false);
            await idle.ConfigureAwait(false);
        }
    }

    private static async Task HandleAsync(NamedPipeServerStream pipe, DaemonIdentity identity,
        WorkspaceCoordinator coordinator, CancellationTokenSource shutdown)
    {
        await using var connection = pipe;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        DaemonRequest? request = null;
        var stop = false;
        try
        {
            request = await Protocol.ReadAsync<DaemonRequest>(pipe, ct, maximumBytes: 64 * 1024).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128)
                throw new InvalidDataException("Missing or invalid request identifier.");
            if (request.ProtocolVersion != DaemonIdentity.ProtocolVersion
                || (request.Method != "Shutdown" && request.BuildId != DaemonIdentity.BuildId))
                throw new InvalidDataException("Incompatible daemon protocol or implementation. Stop the old daemon and retry.");
            if (!string.Equals(request.Root, identity.Root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Daemon repository identity mismatch.");
            SnapshotResult? analysis = null;
            switch (request.Method)
            {
                case "Handshake": case "Status": break;
                case "Check": analysis = await CheckConnectedAsync(pipe, coordinator, timeout).ConfigureAwait(false); break;
                case "Shutdown": stop = true; break;
                default: throw new InvalidDataException("Unknown daemon operation.");
            }
            await Protocol.WriteAsync(pipe, new DaemonResponse(DaemonIdentity.ProtocolVersion, request.RequestId,
                "completed", DaemonIdentity.BuildId, Environment.ProcessId, analysis), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try
            {
                await Protocol.WriteAsync(pipe, new DaemonResponse(DaemonIdentity.ProtocolVersion, request?.RequestId ?? "",
                    "error", DaemonIdentity.BuildId, Environment.ProcessId, Error: ex.Message), ct).ConfigureAwait(false);
            }
            catch (Exception writeError) when (writeError is IOException or OperationCanceledException or JsonException) { }
        }
        finally { if (stop) await shutdown.CancelAsync().ConfigureAwait(false); }
    }

    private static async Task<SnapshotResult> CheckConnectedAsync(NamedPipeServerStream pipe,
        WorkspaceCoordinator coordinator, CancellationTokenSource request)
    {
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
        var monitor = MonitorAsync();
        try { return await coordinator.CheckAsync(cancellationToken: request.Token).ConfigureAwait(false); }
        finally
        {
            await monitorCancellation.CancelAsync().ConfigureAwait(false);
            await monitor.ConfigureAwait(false);
        }

        async Task MonitorAsync()
        {
            try
            {
                // A second frame is invalid; EOF means this request has no remaining consumer.
                await pipe.ReadAsync(new byte[1], monitorCancellation.Token).ConfigureAwait(false);
                await request.CancelAsync().ConfigureAwait(false);
            }
            catch (IOException) { await request.CancelAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) when (monitorCancellation.IsCancellationRequested) { }
        }
    }
}
