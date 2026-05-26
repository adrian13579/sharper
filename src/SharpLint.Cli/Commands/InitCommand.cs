using Spectre.Console;
using Spectre.Console.Cli;
using SharpLint.Configuration;

namespace SharpLint.Cli.Commands;

public sealed class InitCommand : Command<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    private const string Template = """
        #:package SharpLint.Configuration@1.0.0-dev

        using SharpLint.Configuration;

        Lint.Configure(config =>
        {
            config.UsePreset(Preset.Recommended);
        }).Apply();
        """;

    public override int Execute(CommandContext context, Settings settings)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), ConfigLoader.DefaultFileName);
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[yellow]{ConfigLoader.DefaultFileName} already exists.[/]");
            return 1;
        }

        File.WriteAllText(path, Template);
        AnsiConsole.MarkupLine($"[green]Created[/] {ConfigLoader.DefaultFileName}");
        return 0;
    }
}
