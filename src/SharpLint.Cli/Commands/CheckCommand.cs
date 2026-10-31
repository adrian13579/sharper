using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using SharpLint.Configuration;
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

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: pretty | json. Defaults to pretty.")]
        [DefaultValue("pretty")]
        public string Format { get; init; } = "pretty";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var workingDirectory = Path.GetFullPath(settings.Path ?? Directory.GetCurrentDirectory());

        var configPath = Path.Combine(workingDirectory, ConfigLoader.DefaultFileName);
        var config = File.Exists(configPath)
            ? await ConfigLoader.LoadAsync(configPath).ConfigureAwait(false)
            : ConfigLoader.Default();

        var analyzer = new Analyzer();
        var analysisContext = new AnalysisContext
        {
            WorkingDirectory = workingDirectory,
            NamingRules = config.NamingRules,
            Rules = config.Rules ?? [],
        };

        var result = await analyzer.AnalyzeAsync(analysisContext).ConfigureAwait(false);

        if (settings.Format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            RenderJson(result);
        }
        else
        {
            RenderPretty(result);
        }

        return result.HasErrors ? 1 : 0;
    }

    private static void RenderPretty(AnalysisResult result)
    {
        AnsiConsole.MarkupLine($"[dim]Files analyzed:[/] {result.FilesAnalyzed}");
        AnsiConsole.MarkupLine($"[dim]Elapsed:[/] {result.Elapsed.TotalMilliseconds:F0} ms");
        AnsiConsole.WriteLine();

        if (result.Diagnostics.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No issues found.[/]");
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
                $"{Markup.Escape(diag.Message)}");
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(diag.Location.FilePath)}:{diag.Location.Line}:{diag.Location.Column}[/]");
            if (!string.IsNullOrEmpty(diag.Suggestion))
            {
                AnsiConsole.MarkupLine($"  [dim]suggestion:[/] {Markup.Escape(diag.Suggestion)}");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{result.Diagnostics.Count}[/] diagnostic(s).");
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private static void RenderJson(AnalysisResult result)
    {
        var payload = new
        {
            filesAnalyzed = result.FilesAnalyzed,
            elapsedMs = result.Elapsed.TotalMilliseconds,
            diagnostics = result.Diagnostics.Select(d => new
            {
                ruleId = d.RuleId,
                category = d.CategoryId,
                severity = d.Severity.ToString().ToLowerInvariant(),
                message = d.Message,
                file = d.Location.FilePath,
                line = d.Location.Line,
                column = d.Location.Column,
                suggestion = d.Suggestion,
            }),
        };

        var json = JsonSerializer.Serialize(payload, s_jsonOptions);
        Console.WriteLine(json);
    }
}
