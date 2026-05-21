using Spectre.Console.Cli;
using SharpLint.Cli.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("sharplint");

    config.AddCommand<CheckCommand>("check")
        .WithDescription("Analyze the repository for naming violations.");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Create a starter sharplint.config.cs in the current directory.");
});

return await app.RunAsync(args);
