using Spectre.Console.Cli;
using SharpLint.Cli.Commands;

if (args.Length == 3 && args[0] == "--daemon-host")
{
    try
    {
        SharpLint.Daemon.DaemonProcess.DetachFromTerminal();
        var identity = SharpLint.Daemon.DaemonIdentity.Create(args[1]);
        var idle = TimeSpan.FromSeconds(double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        await new SharpLint.Daemon.DaemonHost().RunAsync(identity, idle);
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }
}

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("sharplint");

    config.AddCommand<CheckCommand>("check")
        .WithDescription("Analyze the repository for naming violations.");

    config.AddBranch("daemon", daemon =>
    {
        daemon.AddCommand<DaemonStartCommand>("start").WithDescription("Start the repository daemon.");
        daemon.AddCommand<DaemonStopCommand>("stop").WithDescription("Stop the repository daemon.");
        daemon.AddCommand<DaemonStatusCommand>("status").WithDescription("Show repository daemon status.");
    });

    config.AddCommand<InitCommand>("init")
        .WithDescription("Create a starter sharplint.config.cs in the current directory.");
});

return await app.RunAsync(args);
