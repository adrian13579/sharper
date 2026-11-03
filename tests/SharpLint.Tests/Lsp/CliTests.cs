using System.Text.Json;
using System.Xml.Linq;

namespace SharpLint.Tests.Lsp;

public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-cli-" + Guid.NewGuid().ToString("N"));
    private static string Cli => RoslynFixture.CliPath;
    public CliTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Version_reports_the_tool_package_version_without_starting_Roslyn()
    {
        var project = XDocument.Load(Path.Combine(ServiceTests.RepositoryRoot(), "src/SharpLint.Cli/SharpLint.Cli.csproj"));
        var result = await Run("--version");
        Assert.Equal(0, result.Code);
        Assert.Equal(project.Descendants("Version").Single().Value, result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("status")]
    [InlineData("daemon", "status")]
    [InlineData("mcp", "--unknown")]
    [InlineData("mcp", "--timeout", "NaN")]
    [InlineData("mcp", "--timeout", "0")]
    [InlineData("mcp", "--timeout", "3601")]
    [InlineData("mcp", "--server")]
    [InlineData("mcp", "--timeout=1", "--timeout=2")]
    [InlineData("mcp", "--solution", "Example.slnx")]
    [InlineData("setup", "claude")]
    [InlineData("setup", "copilot", "--client", "unknown")]
    public async Task Invalid_options_produce_errors_and_exit_two(params string[] arguments)
    {
        var result = await Run(arguments);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task Help_describes_MCP_and_agent_setup()
    {
        var result = await Run("--help");
        Assert.Equal(0, result.Code);
        Assert.Contains("Usage: sharplint", result.Output);
        Assert.Contains("setup copilot", result.Output);
        Assert.Contains("MCP server", result.Output);
        Assert.DoesNotContain("check [FILE", result.Output);
    }

    [Fact]
    public async Task Setup_merges_both_clients_without_pin_to_a_repository_and_preserves_JSONC()
    {
        var cli = Path.Combine(_root, "copilot", "mcp-config.json");
        var vscode = Path.Combine(_root, "Code", "User", "mcp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(vscode)!);
        File.WriteAllText(vscode, """
            // Preserve a comment with an opening { brace.
            {
              // This other server belongs to the user.
              "servers": { "other": { "command": "existing" }, },
              "inputs": [{ "id": "token", "type": "promptString" }],
            }
            """);
        var arguments = new[] { "setup", "copilot", "--copilot-config", cli, "--vscode-config", vscode };
        var result = await Run(arguments);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Error);
        using var cliJson = Read(cli);
        using var editor = Read(vscode);
        var config = cliJson.RootElement.GetProperty("mcpServers").GetProperty("sharplint");
        Assert.Equal("stdio", config.GetProperty("type").GetString());
        Assert.True(Path.IsPathFullyQualified(config.GetProperty("command").GetString()!));
        Assert.Contains(config.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "mcp");
        Assert.DoesNotContain(config.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "--workspace");
        Assert.Equal("*", config.GetProperty("tools")[0].GetString());
        Assert.Equal("existing", editor.RootElement.GetProperty("servers").GetProperty("other").GetProperty("command").GetString());
        Assert.Equal("token", editor.RootElement.GetProperty("inputs")[0].GetProperty("id").GetString());
        Assert.Contains("// This other server belongs to the user.", File.ReadAllText(vscode));
        Assert.Contains("// Preserve a comment with an opening { brace.", File.ReadAllText(vscode));
        var beforeCli = File.ReadAllText(cli);
        var beforeCode = File.ReadAllText(vscode);
        Assert.Equal(0, (await Run(arguments)).Code);
        Assert.Equal(beforeCli, File.ReadAllText(cli));
        Assert.Equal(beforeCode, File.ReadAllText(vscode));
    }

    [Fact]
    public async Task Setup_updates_owned_entry_and_keeps_environment_and_other_servers()
    {
        var config = Path.Combine(_root, "mcp.json");
        File.WriteAllText(config, """
            {"servers":{"other":{"command":"keep"},"sharplint":{"command":"old","args":["mcp","--workspace","old"],"env":{"KEEP":"value"}}}}
            """);
        var result = await Run("setup", "copilot", "--client", "vscode", "--vscode-config", config, "--server", "custom-server");
        Assert.Equal(0, result.Code);
        using var json = Read(config);
        var servers = json.RootElement.GetProperty("servers");
        Assert.Equal("keep", servers.GetProperty("other").GetProperty("command").GetString());
        var server = servers.GetProperty("sharplint");
        Assert.Equal("value", server.GetProperty("env").GetProperty("KEEP").GetString());
        Assert.Contains(server.GetProperty("args").EnumerateArray(), a => a.GetString() == "custom-server");
        Assert.DoesNotContain(server.GetProperty("args").EnumerateArray(), a => a.GetString() == "--workspace");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"servers\":null}")]
    [InlineData("{\"servers\":[]}")]
    [InlineData("{\"servers\":{\"sharplint\":\"keep\"}}")]
    public async Task Setup_rejects_malformed_configuration_before_writing_either_file(string contents)
    {
        var cli = Path.Combine(_root, "mcp-config.json");
        var code = Path.Combine(_root, "mcp.json");
        File.WriteAllText(code, contents);
        var result = await Run("setup", "copilot", "--copilot-config", cli, "--vscode-config", code);
        Assert.Equal(2, result.Code);
        Assert.Equal(contents, File.ReadAllText(code));
        Assert.False(File.Exists(cli));
    }

    [Fact]
    public async Task Setup_rejects_shared_destination_without_writing()
    {
        var target = Path.Combine(_root, "same.json");
        Assert.Equal(2, (await Run("setup", "copilot", "--copilot-config", target, "--vscode-config", target)).Code);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task Setup_uses_COPILOT_HOME_for_the_default_user_configuration()
    {
        var home = Path.Combine(_root, "copilot-home");
        var result = await RoslynFixture.RunAsync("dotnet", [Cli, "setup", "copilot", "--client", "cli"], _root,
            new Dictionary<string, string> { ["COPILOT_HOME"] = home });
        Assert.Equal(0, result.Code);
        using var json = Read(Path.Combine(home, "mcp-config.json"));
        Assert.True(json.RootElement.GetProperty("mcpServers").TryGetProperty("sharplint", out _));
    }

    private Task<(int Code, string Output, string Error)> Run(params string[] args) => RoslynFixture.RunAsync("dotnet", [Cli, .. args], _root);
    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
}
