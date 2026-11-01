using System.Globalization;
using SharpLint.Analysis;
using SharpLint.Daemon;

try
{
    if (args.Length == 3 && args[0] == "--daemon-host")
    {
        DaemonProcess.DetachFromTerminal();
        var identity = DaemonIdentity.Create(args[1]);
        // Capture the implementation fingerprint before a subsequent build can replace files.
        _ = DaemonIdentity.BuildId;
        await new DaemonHost().RunAsync(identity, TimeSpan.FromSeconds(double.Parse(args[2], CultureInfo.InvariantCulture)));
        return 0;
    }
    if (args.Length == 2 && args[0] == "--check")
    {
        using var coordinator = new WorkspaceCoordinator(args[1], watch: false);
        var snapshot = await coordinator.CheckAsync(full: true);
        await Protocol.WriteAsync(Console.OpenStandardOutput(), new DaemonResponse(DaemonIdentity.ProtocolVersion,
            "oneshot", "completed", DaemonIdentity.BuildId, Environment.ProcessId,
            AnalysisResponseAdapter.FromSnapshot(snapshot)), default);
        return 0;
    }
    Console.Error.WriteLine("Use the sharplint CLI to start the daemon or run analysis.");
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
