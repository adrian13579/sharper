using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using SharpLint.Daemon;

namespace SharpLint.Cli.Commands;

public sealed class CheckCommand
{
    private static readonly CliJsonContext s_statsContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public sealed class Settings
    {
        public string? Path { get; init; }

        public bool NoDaemon { get; init; }

        public bool Stats { get; init; }

        public string Format { get; init; } = "pretty";
    }

    public async Task<int> ExecuteAsync(Settings settings)
    {
        var total = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var identity = DaemonIdentity.Create(settings.Path ?? Directory.GetCurrentDirectory());
            AnalysisResponse snapshot;
            var connection = Stopwatch.StartNew();
            double connectionMs;
            if (settings.NoDaemon)
            {
                connectionMs = 0;
                snapshot = await new DaemonClient(identity).CheckOnceAsync(cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                var client = new DaemonClient(identity);
                await client.EnsureStartedAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
                connectionMs = connection.Elapsed.TotalMilliseconds;
                var response = await client.SendAsync("Check", cancellationToken: cancellation.Token).ConfigureAwait(false);
                snapshot = response.Analysis ?? throw new DaemonCommunicationException("Daemon returned no analysis result.");
            }
            var metadata = settings.Stats ? new CliStatistics(snapshot.SnapshotVersion, snapshot.Validated,
                snapshot.InputFingerprint, !settings.NoDaemon, snapshot.Statistics, connectionMs,
                total.Elapsed.TotalMilliseconds) : null;
            if (settings.Format.Equals("json", StringComparison.OrdinalIgnoreCase)) RenderJson(snapshot.Result, metadata);
            else
            {
                RenderPretty(snapshot.Result);
                if (metadata is not null) Console.WriteLine(JsonSerializer.Serialize(metadata, s_statsContext.CliStatistics));
            }
            return !snapshot.Result.IsComplete ? 2 : snapshot.Result.HasErrors ? 1 : 0;
        }
        catch (DaemonCommunicationException ex) { Console.Error.WriteLine(ex.Message); return 3; }
        catch (OperationCanceledException) { Console.Error.WriteLine("Analysis canceled."); return 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    private static void RenderPretty(ResultResponse result)
    {
        foreach (var failure in result.Failures)
            AnsiConsole.MarkupLine($"[red]Incomplete analysis:[/] {Markup.Escape(failure.RuleId)}: {Markup.Escape(failure.Message)}");
        AnsiConsole.MarkupLine($"[dim]Files analyzed:[/] {result.FilesAnalyzed}");
        AnsiConsole.MarkupLine($"[dim]Elapsed:[/] {result.ElapsedMs:F0} ms");
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
                "error" => "red",
                "warning" => "yellow",
                "info" => "blue",
                _ => "grey",
            };
            AnsiConsole.MarkupLine(
                $"[{color}]{diag.Severity}[/] " +
                $"[bold]{Markup.Escape(diag.RuleId)}[/] " +
                $"[[{(diag.Origin == "compiler" ? "Compiler" : "SharpLint")}]]");
            AnsiConsole.MarkupLine($"  {Markup.Escape(diag.Message)}");
            if (diag.File is not null)
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(diag.File!)}:{diag.Line}:{diag.Column}[/]");
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

    private static void RenderJson(ResultResponse result, CliStatistics? statistics)
    {
        var payload = new CheckOutput(result.IsComplete ? "completed" : "incomplete", result.Failures,
            result.FilesAnalyzed, result.ElapsedMs, result.Diagnostics, statistics);
        Console.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.CheckOutput));
    }
}
