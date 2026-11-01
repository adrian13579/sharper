using System.ComponentModel;
using SharpLint.Daemon;
using Spectre.Console.Cli;

namespace SharpLint.Cli.Commands;

public class DaemonSettings : CommandSettings
{
    [CommandOption("--path <PATH>")]
    [Description("Repository directory. Defaults to the current directory.")]
    public string? Path { get; init; }

    public DaemonClient Client() => new(DaemonIdentity.Create(Path ?? Directory.GetCurrentDirectory()));
}

public sealed class DaemonStartCommand : AsyncCommand<DaemonStartCommand.Settings>
{
    public sealed class Settings : DaemonSettings
    {
        [CommandOption("--idle-timeout <SECONDS>")]
        [Description("Seconds of inactivity before shutdown. Defaults to 900.")]
        public double IdleTimeout { get; init; } = 900;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!double.IsFinite(settings.IdleTimeout) || settings.IdleTimeout <= 0)
        {
            Console.Error.WriteLine("Idle timeout must be a positive number of seconds.");
            return 2;
        }
        try
        {
            await settings.Client().EnsureStartedAsync(TimeSpan.FromSeconds(settings.IdleTimeout));
            Console.WriteLine("Daemon running.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }
    }
}

public sealed class DaemonStopCommand : AsyncCommand<DaemonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DaemonSettings settings)
    {
        try
        {
            var client = settings.Client();
            if (await client.StatusAsync() is null) { Console.WriteLine("Daemon stopped."); return 0; }
            var stopped = await client.SendAsync("Shutdown");
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(stopped.ProcessId);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (ArgumentException) { /* Already exited. */ }
            Console.WriteLine("Daemon stopped.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }
    }
}

public sealed class DaemonStatusCommand : AsyncCommand<DaemonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DaemonSettings settings)
    {
        try
        {
            var response = await settings.Client().StatusAsync();
            if (response is null) Console.WriteLine("Daemon stopped.");
            else if (response.Status != "completed") { Console.Error.WriteLine(response.Error); return 3; }
            else Console.WriteLine($"Daemon running (PID {response.ProcessId}).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }
    }
}
