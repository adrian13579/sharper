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
            if (args.Length == 0 || args.Any(a => a is "--help" or "-h"))
            {
                Console.WriteLine("""
                    Usage: sharplint <check [FILE|DIRECTORY] | status | mcp> [options]
                      --path <PATH>          Check scope (default: workspace directory)
                      --workspace <PATH>     Synchronization and project root (default: current directory)
                      --solution <PATH>      Select a .sln or .slnx; otherwise Roslyn auto-loads projects
                      --server <PATH>        Official roslyn-language-server executable or DLL
                      --timeout <SECONDS>    Deadline including startup (default: 120)
                      --format <FORMAT>      pretty | json (default: pretty)

                    Checks return C# document pull diagnostics, not full solution/build verification.
                    Positions are zero-based UTF-16. Each CLI process starts its own Roslyn session.
                    MCP stdio keeps one session warm. Restore project dependencies before checking.
                    Exit codes: 0 = complete, no errors; 1 = diagnostic errors; 2 = incomplete/failed; 130 = cancelled.
                    """);
                return 0;
            }
            if (args is ["--version"])
            {
                var version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
                Console.WriteLine(version.Split('+')[0]);
                return 0;
            }
            var command = args[0];
            if (command is not ("check" or "status" or "mcp")) throw new ArgumentException($"Unknown command: {command}");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            string? positional = null;
            for (var i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    if (command != "check" || positional is not null) throw new ArgumentException($"Unexpected argument: {args[i]}");
                    positional = args[i];
                    continue;
                }
                var pair = args[i].Split('=', 2);
                if (pair[0] is not ("--path" or "--workspace" or "--solution" or "--server" or "--timeout" or "--format"))
                    throw new ArgumentException($"Unknown option: {pair[0]}");
                string value;
                if (pair.Length == 2) value = pair[1];
                else if (++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)) value = args[i];
                else throw new ArgumentException($"Missing value for {pair[0]}.");
                if (string.IsNullOrWhiteSpace(value) || !options.TryAdd(pair[0], value)) throw new ArgumentException($"Invalid or duplicate option: {pair[0]}");
            }
            if (positional is not null && options.ContainsKey("--path")) throw new ArgumentException("Specify the check path once.");
            if (command != "check" && options.ContainsKey("--path")) throw new ArgumentException("Use --workspace to select the workspace for status or mcp.");
            var format = options.GetValueOrDefault("--format", "pretty");
            if (format is not ("pretty" or "json")) throw new ArgumentException("Format must be pretty or json.");
            if (!double.TryParse(options.GetValueOrDefault("--timeout", "120"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || !double.IsFinite(seconds) || seconds <= 0 || seconds > 3600) throw new ArgumentException("Timeout must be a positive number of seconds up to 3600.");
            var path = positional ?? options.GetValueOrDefault("--path");
            var root = options.GetValueOrDefault("--workspace")
                ?? (path is not null && Directory.Exists(path) ? path : Directory.GetCurrentDirectory());
            var settings = new DiagnosticServiceOptions
            {
                RootPath = root, SolutionPath = options.GetValueOrDefault("--solution"),
                ServerPath = options.GetValueOrDefault("--server"), Timeout = TimeSpan.FromSeconds(seconds),
            };
            if (command == "mcp")
            {
                var builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                builder.Services.AddSingleton(_ => new DiagnosticService(settings));
                builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<DiagnosticTools>();
                using var host = builder.Build();
                await host.RunAsync(cancellation.Token);
                return 0;
            }
            await using var service = new DiagnosticService(settings);
            if (command == "status")
            {
                var status = await service.StatusAsync(cancellation.Token);
                Console.WriteLine(format == "json" ? JsonSerializer.Serialize(status, DiagnosticJson.Options)
                    : $"Roslyn: {(status.Available ? "available" : "unavailable")}; workspace: {status.WorkspaceState}; document diagnostics: {status.DiagnosticsAvailable}\n{status.LastError}");
                return status.Available && status.DiagnosticsAvailable && status.LastError is null ? 0 : 2;
            }
            var response = await service.CheckAsync(path is null ? null : Path.GetFullPath(path), cancellation.Token);
            if (format == "json") Console.WriteLine(JsonSerializer.Serialize(response, DiagnosticJson.Options));
            else
            {
                foreach (var diagnostic in response.Diagnostics)
                    Console.WriteLine($"{diagnostic.FilePath ?? "<project>"}({diagnostic.StartLine?.ToString(CultureInfo.InvariantCulture) ?? "-"},{diagnostic.StartColumn?.ToString(CultureInfo.InvariantCulture) ?? "-"}): {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
                Console.WriteLine($"{response.Status}; scope: {response.Scope}; complete: {response.Complete}; freshness: {response.Freshness}; {response.DiagnosticCount} diagnostics; {response.DurationMs:F0} ms");
                foreach (var error in response.Errors) Console.Error.WriteLine(error);
                foreach (var limitation in response.Limitations) Console.WriteLine(limitation);
            }
            return response.Status == "cancelled" ? 130 : !response.Complete ? 2 : response.Diagnostics.Any(d => d.Severity == "error") ? 1 : 0;
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
