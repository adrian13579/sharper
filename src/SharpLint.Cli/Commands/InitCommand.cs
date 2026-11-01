using Spectre.Console;

namespace SharpLint.Cli.Commands;

public sealed class InitCommand
{
    private const string ConfigFileName = "sharplint.config.cs";

    private const string Template = """
        #:package SharpLint.Configuration@1.0.0-dev

        using SharpLint.Configuration;

        Lint.Configure(config =>
        {
            config.UsePreset(Preset.Recommended);
        }).Apply();
        """;

    public int Execute()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[yellow]{ConfigFileName} already exists.[/]");
            return 1;
        }

        File.WriteAllText(path, Template);
        AnsiConsole.MarkupLine($"[green]Created[/] {ConfigFileName}");
        return 0;
    }
}
