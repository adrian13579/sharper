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
        using SharpLint.Core.Naming;

        Lint.Configure(config =>
        {
            config.UsePreset(Preset.Recommended);

            config.Naming(n =>
            {
                n.Classes().RequireConvention(NamingConvention.PascalCase);
                n.Interfaces()
                    .RequirePrefix("I")
                    .RequireConvention(NamingConvention.PascalCase);
                n.Methods(m => m.Public()).RequireConvention(NamingConvention.PascalCase);
                n.Methods(m => m.Async()).RequireSuffix("Async");
                n.Properties().RequireConvention(NamingConvention.PascalCase);
                n.Fields(f => f.Private())
                    .RequirePrefix("_")
                    .RequireConvention(NamingConvention.CamelCase);
                n.Fields(f => f.Const()).RequireConvention(NamingConvention.UpperSnakeCase);
                n.Parameters().RequireConvention(NamingConvention.CamelCase);
                n.LocalVariables().RequireConvention(NamingConvention.CamelCase);
            });
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
