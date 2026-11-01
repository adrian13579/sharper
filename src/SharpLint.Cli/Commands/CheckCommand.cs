using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using SharpLint.Analysis;
using SharpLint.Daemon;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Cli.Commands;

public sealed class CheckCommand : AsyncCommand<CheckCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--path <PATH>")]
        [Description("Path to analyze. Defaults to current directory.")]
        public string? Path { get; init; }

        [CommandOption("--no-daemon")]
        [Description("Run an independent complete analysis in this process.")]
        public bool NoDaemon { get; init; }

        [CommandOption("--stats")]
        [Description("Include snapshot, cache and timing metadata.")]
        public bool Stats { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: pretty | json. Defaults to pretty.")]
        [DefaultValue("pretty")]
        public string Format { get; init; } = "pretty";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var total = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var identity = DaemonIdentity.Create(settings.Path ?? Directory.GetCurrentDirectory());
            SnapshotResult snapshot;
            var connection = Stopwatch.StartNew();
            double connectionMs;
            if (settings.NoDaemon)
            {
                using var coordinator = new WorkspaceCoordinator(identity.Root, watch: false);
                connectionMs = 0;
                snapshot = await coordinator.CheckAsync(full: true, cancellationToken: cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                var client = new DaemonClient(identity);
                await client.EnsureStartedAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
                connectionMs = connection.Elapsed.TotalMilliseconds;
                var response = await client.SendAsync("Check", cancellationToken: cancellation.Token).ConfigureAwait(false);
                snapshot = response.Analysis ?? throw new DaemonCommunicationException("Daemon returned no analysis result.");
            }
            var metadata = settings.Stats ? new
            {
                snapshotVersion = snapshot.SnapshotVersion,
                validated = snapshot.Validated,
                inputFingerprint = snapshot.InputFingerprint,
                daemonConnected = !settings.NoDaemon,
                snapshot.Statistics.DocumentsAnalyzed,
                snapshot.Statistics.DocumentsReused,
                snapshot.Statistics.CacheHits,
                snapshot.Statistics.ProjectsAnalyzed,
                snapshot.Statistics.ProjectsReused,
                snapshot.Statistics.ProjectsInWorkspace,
                snapshot.Statistics.CompilerDiagnostics,
                snapshot.Statistics.SharpLintDiagnostics,
                snapshot.Statistics.ReconciliationMs,
                snapshot.Statistics.WorkspaceUpdateMs,
                snapshot.Statistics.RuleExecutionMs,
                snapshot.Statistics.AggregationMs,
                snapshot.Statistics.TotalMs,
                connectionMs,
                cliElapsedMs = total.Elapsed.TotalMilliseconds,
            } : null;
            if (settings.Format.Equals("json", StringComparison.OrdinalIgnoreCase)) RenderJson(snapshot.Result, metadata);
            else
            {
                RenderPretty(snapshot.Result);
                if (metadata is not null) Console.WriteLine(JsonSerializer.Serialize(metadata, s_statsOptions));
            }
            return !snapshot.Result.IsComplete ? 2 : snapshot.Result.HasErrors ? 1 : 0;
        }
        catch (DaemonCommunicationException ex) { Console.Error.WriteLine(ex.Message); return 3; }
        catch (OperationCanceledException) { Console.Error.WriteLine("Analysis canceled."); return 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    private static void RenderPretty(AnalysisResult result)
    {
        foreach (var failure in result.Failures)
            AnsiConsole.MarkupLine($"[red]Incomplete analysis:[/] {Markup.Escape(failure.RuleId)}: {Markup.Escape(failure.Message)}");
        AnsiConsole.MarkupLine($"[dim]Files analyzed:[/] {result.FilesAnalyzed}");
        AnsiConsole.MarkupLine($"[dim]Elapsed:[/] {result.Elapsed.TotalMilliseconds:F0} ms");
        AnsiConsole.WriteLine();

        if (result.Diagnostics.Count == 0)
        {
            if (result.IsComplete) AnsiConsole.MarkupLine("[green]No issues found.[/]");
            return;
        }

        foreach (var diag in result.Diagnostics)
        {
            var color = diag.Severity switch
            {
                Severity.Error => "red",
                Severity.Warning => "yellow",
                Severity.Info => "blue",
                _ => "grey",
            };
            AnsiConsole.MarkupLine(
                $"[{color}]{diag.Severity.ToString().ToLowerInvariant()}[/] " +
                $"[bold]{Markup.Escape(diag.RuleId)}[/] " +
                $"[[{diag.Origin}]]");
            AnsiConsole.MarkupLine($"  {Markup.Escape(diag.Message)}");
            if (diag.HasSourceLocation)
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(diag.Location.FilePath)}:{diag.Location.Line}:{diag.Location.Column}[/]");
            if (diag.Project is not null)
                AnsiConsole.MarkupLine($"  [dim]project: {Markup.Escape(diag.Project)}[/]");
            if (!string.IsNullOrEmpty(diag.Suggestion))
            {
                AnsiConsole.MarkupLine($"  [dim]suggestion:[/] {Markup.Escape(diag.Suggestion)}");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{result.Diagnostics.Count}[/] diagnostic(s).");
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions s_statsOptions = new(JsonSerializerDefaults.Web);

    private static void RenderJson(AnalysisResult result, object? statistics)
    {
        var payload = new Dictionary<string, object?>
        {
            ["status"] = result.IsComplete ? "completed" : "incomplete",
            ["failures"] = result.Failures.Select(f => new { ruleId = f.RuleId, message = f.Message }),
            ["filesAnalyzed"] = result.FilesAnalyzed,
            ["elapsedMs"] = result.Elapsed.TotalMilliseconds,
            ["diagnostics"] = result.Diagnostics.Select(d => new
            {
                ruleId = d.RuleId,
                origin = d.Origin == DiagnosticOrigin.Compiler ? "compiler" : "sharplint",
                category = d.CategoryId,
                module = d.ModuleId,
                severity = d.Severity.ToString().ToLowerInvariant(),
                message = d.Message,
                file = d.HasSourceLocation ? d.Location.FilePath : null,
                line = d.HasSourceLocation ? (int?)d.Location.Line : null,
                column = d.HasSourceLocation ? (int?)d.Location.Column : null,
                project = d.Project,
                helpUrl = d.HelpUrl,
                spanStart = d.SpanStart,
                spanLength = d.SpanLength,
                suggestion = d.Suggestion,
            }),
        };

        if (statistics is not null)
            payload["statistics"] = JsonSerializer.SerializeToElement(statistics, s_statsOptions);
        var json = JsonSerializer.Serialize(payload, s_jsonOptions);
        Console.WriteLine(json);
    }
}
