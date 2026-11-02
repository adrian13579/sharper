using System.Text.Json;

namespace SharpLint.Tests.Lsp;

public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-cli-" + Guid.NewGuid().ToString("N"));
    private static string Cli => RoslynFixture.CliPath;
    private static string Server => Path.Combine(ServiceTests.RepositoryRoot(), "tests/SharpLint.FakeServer/bin/Debug/net10.0/SharpLint.FakeServer.dll");
    public CliTests() { Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "Sample.cs"), "missing"); }
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("check", "--unknown")]
    [InlineData("check", "--path", "--format", "json")]
    [InlineData("check", "--format", "xml")]
    [InlineData("check", "--timeout", "NaN")]
    [InlineData("check", "--timeout", "0")]
    [InlineData("status", "--path", ".")]
    [InlineData("check", "Sample.cs", "--path", "Sample.cs")]
    [InlineData("check", "--format=json", "--format=json")]
    [InlineData("daemon", "status")]
    public async Task Invalid_options_produce_errors_and_exit_two(params string[] arguments)
    {
        var result = await RoslynFixture.RunAsync("dotnet", [Cli, .. arguments], _root);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task Json_pretty_status_exit_codes_and_help_match_the_shared_service()
    {
        var arguments = new[] { "--server", Server, "--format=json" };
        var errors = await RoslynFixture.RunAsync("dotnet", [Cli, "check", "Sample.cs", .. arguments], _root);
        Assert.Equal(1, errors.Code);
        using var errorJson = JsonDocument.Parse(errors.Output);
        Assert.Equal("CS0103", errorJson.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.True(errorJson.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal("file", errorJson.RootElement.GetProperty("scope").GetString());
        File.WriteAllText(Path.Combine(_root, "Sample.cs"), "fixed");
        var fixedResult = await RoslynFixture.RunAsync("dotnet", [Cli, "check", .. arguments], _root);
        Assert.Equal(0, fixedResult.Code);
        using var fixedJson = JsonDocument.Parse(fixedResult.Output);
        Assert.Equal(0, fixedJson.RootElement.GetProperty("diagnosticCount").GetInt32());
        var pretty = await RoslynFixture.RunAsync("dotnet", [Cli, "check", "--server", Server], _root);
        Assert.Equal(0, pretty.Code);
        Assert.Contains("completed", pretty.Output);
        Assert.Contains("freshness: verified", pretty.Output);
        var status = await RoslynFixture.RunAsync("dotnet", [Cli, "status", .. arguments], _root);
        Assert.Equal(0, status.Code);
        using var statusJson = JsonDocument.Parse(status.Output);
        Assert.True(statusJson.RootElement.GetProperty("available").GetBoolean());
        var help = await RoslynFixture.RunAsync("dotnet", [Cli, "check", "--help"], _root);
        Assert.Equal(0, help.Code);
        Assert.Contains("Usage: sharplint", help.Output);
        var unavailable = await RoslynFixture.RunAsync("dotnet", [Cli, "check", "--server", Path.Combine(_root, "absent"), "--format=json"], _root);
        Assert.Equal(2, unavailable.Code);
        using var unavailableJson = JsonDocument.Parse(unavailable.Output);
        Assert.Equal("failed", unavailableJson.RootElement.GetProperty("status").GetString());
    }
}
