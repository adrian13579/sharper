using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace SharpLint.Daemon;

public sealed class DaemonCommunicationException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class DaemonAnalysisException(string message) : Exception(message);

public sealed class DaemonClient(DaemonIdentity identity)
{
    public async Task<AnalysisResponse> CheckOnceAsync(CancellationToken cancellationToken = default)
    {
        var start = HostStartInfo();
        start.ArgumentList.Add("--check");
        start.ArgumentList.Add(identity.Root);
        using var process = StartHost(start);
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            DaemonResponse? response = null;
            try { response = await Protocol.ReadAsync<DaemonResponse>(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false); }
            catch (EndOfStreamException) { /* Report the managed worker's error below. */ }
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0 || response?.Analysis is null)
                throw new DaemonAnalysisException($"Analysis process failed (exit code {process.ExitCode}). {error}");
            Validate(response);
            return response.Analysis;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task<DaemonResponse> SendAsync(string method, bool autoStart = false,
        TimeSpan? idleTimeout = null, CancellationToken cancellationToken = default)
    {
        if (autoStart) await EnsureStartedAsync(idleTimeout, cancellationToken).ConfigureAwait(false);
        var response = await TrySendAsync(method, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        if (response is null)
            throw new DaemonCommunicationException("Daemon is unavailable. Run 'sharplint daemon start' or retry the check.");
        if (method != "Shutdown") Validate(response);
        else if (response.Status != "completed") throw new DaemonCommunicationException(response.Error ?? "Shutdown failed.");
        return response;
    }

    public Task<DaemonResponse?> StatusAsync(CancellationToken cancellationToken = default) =>
        TrySendAsync("Status", TimeSpan.FromMilliseconds(200), cancellationToken);

    public async Task EnsureStartedAsync(TimeSpan? idleTimeout = null, CancellationToken cancellationToken = default)
    {
        var existing = await TrySendAsync("Handshake", TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        if (existing is not null) { Validate(existing); return; }
        identity.EnsureStateDirectory();
        var timer = Stopwatch.StartNew();
        FileStream? startup = null;
        while (startup is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { startup = new FileStream(identity.LockPath("startup"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex)
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(30))
                    throw new DaemonCommunicationException("Timed out acquiring the daemon startup lock.", ex);
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }
        using (startup)
        {
            existing = await TrySendAsync("Handshake", TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            if (existing is not null) { Validate(existing); return; }
            var startupErrors = new StringBuilder();
            using var launched = StartProcess(idleTimeout ?? TimeSpan.FromMinutes(15), startupErrors);
            while (timer.Elapsed < TimeSpan.FromSeconds(30))
            {
                existing = await TrySendAsync("Handshake", TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                if (existing is not null) { Validate(existing); return; }
                if (launched.HasExited)
                {
                    launched.WaitForExit(); // Drain asynchronous startup stderr before reporting the failure.
                    throw new DaemonCommunicationException($"Daemon exited during startup (exit code {launched.ExitCode}). {startupErrors}");
                }
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new DaemonCommunicationException("Daemon startup timed out. Check repository access and the installed .NET runtime.");
    }

    private Process StartProcess(TimeSpan idleTimeout, StringBuilder errors)
    {
        var start = HostStartInfo();
        start.ArgumentList.Add("--daemon-host");
        start.ArgumentList.Add(identity.Root);
        start.ArgumentList.Add(idleTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = StartHost(start);
        process.ErrorDataReceived += (_, args) =>
        {
            lock (errors)
            {
                if (errors.Length < 8192 && args.Data is not null) errors.AppendLine(args.Data);
            }
        };
        process.StandardInput.Close();
        // Drain startup output while this client exists; the detached host never writes routine output.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private ProcessStartInfo HostStartInfo()
    {
        if (!File.Exists(DaemonIdentity.HostPath))
            throw new DaemonCommunicationException($"Managed daemon host missing: {DaemonIdentity.HostPath}");
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = identity.Root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(DaemonIdentity.HostPath);
        return start;
    }

    private static Process StartHost(ProcessStartInfo start)
    {
        try { return Process.Start(start) ?? throw new DaemonCommunicationException("Failed to start the daemon process."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new DaemonCommunicationException("Failed to start the .NET daemon host.", ex); }
    }

    private async Task<DaemonResponse?> TrySendAsync(string method, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", identity.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(connectTimeout);
            try { await pipe.ConnectAsync(connect.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            catch (IOException) { return null; } // A crashed server can leave a stale OS endpoint.
            var id = Guid.NewGuid().ToString("N");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await Protocol.WriteAsync(pipe, new DaemonRequest(DaemonIdentity.ProtocolVersion, id, method,
                identity.Root, DaemonIdentity.BuildId), timeout.Token).ConfigureAwait(false);
            var response = await Protocol.ReadAsync<DaemonResponse>(pipe, timeout.Token).ConfigureAwait(false);
            if (response.RequestId != id) throw new DaemonCommunicationException("Daemon response request identity mismatch.");
            return response;
        }
        catch (IOException ex) { throw new DaemonCommunicationException("Daemon connection failed; retry to recover from a crashed daemon.", ex); }
        catch (System.Text.Json.JsonException ex) { throw new DaemonCommunicationException("Invalid daemon response.", ex); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new DaemonCommunicationException("Daemon request timed out.", ex); }
    }

    private static void Validate(DaemonResponse response)
    {
        if (response.ProtocolVersion != DaemonIdentity.ProtocolVersion || response.BuildId != DaemonIdentity.BuildId)
            throw new DaemonCommunicationException("Incompatible daemon. Run 'sharplint daemon stop', then retry.");
        if (response.Status != "completed") throw new DaemonAnalysisException(response.Error ?? "Daemon request failed.");
    }
}
