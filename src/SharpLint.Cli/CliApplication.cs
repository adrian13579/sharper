using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpLint.Analysis;
using SharpLint.Cli.Mcp;

namespace SharpLint.Cli;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                Console.WriteLine("""
                    Sharplint is an MCP server providing Roslyn diagnostic feedback to coding agents.
                    Usage: sharplint [mcp | setup copilot] [options]

                    mcp (also the default with no arguments): start the MCP stdio server
                      --server <PATH>          Official Roslyn executable or DLL
                      --timeout <SECONDS>      Request deadline including session queue (default: 120)
                      --workspace <PATH>       Optional fallback repository; tools can select other repositories
                      --solution <PATH>        Optional solution for the fallback repository

                    setup copilot: register Sharplint for GitHub Copilot
                      --client <CLIENT>        both | cli | vscode (default: both)
                      --copilot-config <PATH>  Override Copilot CLI configuration file
                      --vscode-config <PATH>   Override VS Code user/profile MCP configuration file
                      --server <PATH>          Configure a custom Roslyn executable or DLL

                    MCP tools: sharplint_check and sharplint_status. The agent selects its repository
                    per call or supplies MCP roots. Roslyn sessions stay warm across tool calls.
                    Restore project dependencies before checking. Logs go to stderr; stdout is MCP.
                    """);
                return 0;
            }
            if (args is ["--version"])
            {
                var version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
                Console.WriteLine(version.Split('+')[0]);
                return 0;
            }
            if (args.Length == 0) args = ["mcp"];
            if (args[0] == "setup") return CopilotSetup.Run(args[1..]);
            if (args[0] != "mcp") throw new ArgumentException($"Unknown command: {args[0]}. Start the MCP server with sharplint mcp, or configure your agent with sharplint setup copilot.");
            var options = ParseOptions(args[1..], "--workspace", "--solution", "--server", "--timeout");
            if (!double.TryParse(options.GetValueOrDefault("--timeout", "120"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || !double.IsFinite(seconds) || seconds <= 0 || seconds > 3600) throw new ArgumentException("Timeout must be a positive number of seconds up to 3600.");
            var root = options.TryGetValue("--workspace", out var selected) ? WorkspaceResolver.DirectoryPath(selected) : null;
            if (options.ContainsKey("--solution") && root is null) throw new ArgumentException("Use --workspace with a fallback --solution, or pass solution to an MCP tool call.");
            var settings = new DiagnosticServiceOptions
            {
                RootPath = root ?? Directory.GetCurrentDirectory(), SolutionPath = options.GetValueOrDefault("--solution"),
                ServerPath = options.GetValueOrDefault("--server"), Timeout = TimeSpan.FromSeconds(seconds),
            };
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(_ => new WorkspaceSessions(settings, root));
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<DiagnosticTools>();
            using var host = builder.Build();
            await host.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static Dictionary<string, string> ParseOptions(string[] args, params string[] allowed)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var pair = args[i].Split('=', 2);
            if (!allowed.Contains(pair[0], StringComparer.Ordinal)) throw new ArgumentException($"Unknown option: {pair[0]}");
            string value;
            if (pair.Length == 2) value = pair[1];
            else if (++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)) value = args[i];
            else throw new ArgumentException($"Missing value for {pair[0]}.");
            if (string.IsNullOrWhiteSpace(value) || !options.TryAdd(pair[0], value)) throw new ArgumentException($"Invalid or duplicate option: {pair[0]}");
        }
        return options;
    }
}
