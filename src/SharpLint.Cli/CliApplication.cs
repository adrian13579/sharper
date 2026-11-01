using System.Globalization;
using SharpLint.Cli.Commands;

namespace SharpLint.Cli;

// Explicit command dispatch keeps command/settings discovery independent of reflection.
internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args is ["--help" or "-h"])
            {
                PrintHelp("");
                return 0;
            }
            if (args is ["--version"])
            {
                Console.WriteLine("0.1.0-alpha");
                return 0;
            }
            var command = args[0];
            var offset = 1;
            if (command == "daemon")
            {
                if (args.Length == 1 || args[1] is "--help" or "-h")
                {
                    PrintHelp("daemon");
                    return 0;
                }
                command += " " + args[1];
                offset = 2;
            }
            string[] allowed = command switch
            {
                "check" => ["--path", "--format", "--no-daemon", "--stats"],
                "init" => [],
                "daemon start" => ["--path", "--idle-timeout"],
                "daemon stop" or "daemon status" => ["--path"],
                _ => throw new ArgumentException($"Unknown command: {command}"),
            };
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = offset; i < args.Length; i++)
            {
                if (args[i] is "--help" or "-h")
                {
                    PrintHelp(command);
                    return 0;
                }
                var pair = args[i].Split('=', 2);
                var name = pair[0];
                if (!allowed.Contains(name)) throw new ArgumentException($"Unknown option: {args[i]}");
                var flag = name is "--no-daemon" or "--stats";
                string value;
                if (flag)
                {
                    if (pair.Length != 1) throw new ArgumentException($"Option {name} does not take a value.");
                    value = "true";
                }
                else if (pair.Length == 2) value = pair[1];
                else
                {
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Missing value for {name}.");
                    value = args[i];
                }
                if (!options.TryAdd(name, value)) throw new ArgumentException($"Option specified more than once: {name}");
            }
            var path = options.GetValueOrDefault("--path");
            switch (command)
            {
                case "check":
                    return await new CheckCommand().ExecuteAsync(new CheckCommand.Settings
                    {
                        Path = path,
                        Format = options.GetValueOrDefault("--format", "pretty"),
                        NoDaemon = options.ContainsKey("--no-daemon"),
                        Stats = options.ContainsKey("--stats"),
                    });
                case "init":
                    return new InitCommand().Execute();
                case "daemon start":
                    if (!double.TryParse(options.GetValueOrDefault("--idle-timeout", "900"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var idleTimeout))
                        throw new ArgumentException("Idle timeout must be a number of seconds.");
                    return await new DaemonStartCommand().ExecuteAsync(new DaemonStartCommand.Settings
                    {
                        Path = path,
                        IdleTimeout = idleTimeout,
                    });
                case "daemon stop":
                    return await new DaemonStopCommand().ExecuteAsync(new DaemonSettings { Path = path });
                default:
                    return await new DaemonStatusCommand().ExecuteAsync(new DaemonSettings { Path = path });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintHelp(string command)
    {
        Console.WriteLine($"Usage: sharplint{(command.Length == 0 ? "" : " " + command)} [options]");
        Console.WriteLine(command switch
        {
            "check" => "Analyze the repository.\n  --path <PATH>       Directory to analyze (default: current directory)\n  --format <FORMAT>   pretty | json (default: pretty)\n  --no-daemon         Run an independent complete analysis in a managed process\n  --stats             Include snapshot, cache and timing metadata",
            "init" => "Create a starter sharplint.config.cs in the current directory.",
            "daemon start" => "Start the repository daemon.\n  --path <PATH>       Repository directory\n  --idle-timeout <SECONDS>  Idle shutdown timeout (default: 900)",
            "daemon stop" => "Stop the repository daemon.\n  --path <PATH>       Repository directory",
            "daemon status" => "Show repository daemon status.\n  --path <PATH>       Repository directory",
            "daemon" => "Commands: start, stop, status\nUse sharplint daemon <command> --help for options.",
            _ => "Commands:\n  check     Analyze the repository\n  init      Create a starter configuration\n  daemon    Manage the repository daemon\nUse sharplint <command> --help for options.",
        });
        Console.WriteLine("  -h, --help         Show help");
    }
}
