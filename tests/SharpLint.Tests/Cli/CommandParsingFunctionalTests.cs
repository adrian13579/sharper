using System.Text.Json;

namespace SharpLint.Tests.Cli;

public sealed partial class CheckCommandFunctionalTests
{
    [Theory]
    [InlineData("check")]
    [InlineData("init")]
    [InlineData("daemon")]
    [InlineData("daemon", "start")]
    [InlineData("daemon", "stop")]
    [InlineData("daemon", "status")]
    public async Task Command_help_exits_successfully_without_starting_a_daemon(params string[] command)
    {
        var result = await RunCliAsync([.. command, "--help"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: sharplint " + string.Join(' ', command), result.Stdout);
        Assert.Contains("stopped", (await RunCliAsync("daemon", "status")).Stdout);
        Assert.False(File.Exists(Path.Combine(_workDir, "sharplint.config.cs")));
    }

    [Theory]
    [InlineData("check", "--unknown")]
    [InlineData("check", "--path", "--stats")]
    [InlineData("check", "--stats=true")]
    [InlineData("check", "--stats", "--stats")]
    [InlineData("init", "--path", ".")]
    [InlineData("daemon", "unknown")]
    [InlineData("daemon", "status", "--idle-timeout", "1")]
    [InlineData("daemon", "start", "--idle-timeout", "oops")]
    public async Task Invalid_command_options_fail_without_analysis(params string[] arguments)
    {
        var result = await RunCliAsync(arguments);
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public async Task Equals_options_and_pretty_statistics_work_without_reflection()
    {
        var result = await RunCliAsync("check", "--path=.", "--format=json", "--no-daemon", "--stats");
        Assert.True(result.ExitCode == 0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        Assert.False(json.RootElement.GetProperty("statistics").GetProperty("daemonConnected").GetBoolean());
        Assert.Equal("completed", json.RootElement.GetProperty("status").GetString());
        var pretty = await RunCliAsync("check", "--no-daemon", "--stats");
        Assert.Equal(0, pretty.ExitCode);
        Assert.Contains("No issues found", pretty.Stdout);
        Assert.Contains("\"daemonConnected\":false", pretty.Stdout.Replace(" ", ""));
    }
}
