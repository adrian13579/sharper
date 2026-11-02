using System.Text.Json;
using System.Text.Json.Serialization;
using SharpLint.Configuration;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;
using SharpLint.Tests.Analysis;
using CoreRules = SharpLint.Core.Rules.Rules;

namespace SharpLint.Tests.Cli;

public sealed partial class CheckCommandFunctionalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Executable_configuration_selects_builtin_module_policies(bool fluent)
    {
        WriteNuGetConfig();
        var declaration = fluent
            ? "Lint.Configure(c => c.Design(d => d.Parameters().RequireMaximumCount(1).AsError())).Apply();"
            : "Lint.Configure(c => c.Module(Rules.Design, m => m.Rule(\"design.maximum-parameters\", new { maximum = 1 }, Severity.Error))).Apply();";
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), """
            #:package SharpLint.Configuration@1.0.0-dev
            using SharpLint.Configuration;
            using SharpLint.Core.Diagnostics;
            using SharpLint.Core.Rules;
            """ + Environment.NewLine + declaration);
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "class Sample { void M(int x, int y) {} }");
        var (exit, stdout, stderr) = await RunCliAsync("check", "--path", _workDir, "--format", "json", "--no-daemon");
        Assert.True(exit == 1, stdout + stderr);
        using var document = JsonDocument.Parse(stdout);
        var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("design.maximum-parameters", diagnostic.GetProperty("ruleId").GetString());
        Assert.Equal("design", diagnostic.GetProperty("module").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Check_loads_external_Roslyn_modules_and_builtin_rules_from_config(bool noDaemon)
    {
        var config = Lint.Configure(c => c
            .Module(CoreRules.Design, m => m.Rule("design.maximum-parameters", new { maximum = 1 }, Severity.Error))
            .UseModule(new RuleModule("acme.security", "Acme security", [new RoslynModuleTests.ExternalAnalyzer()]),
                m => m.Rule("ACME001", new { forbiddenBase = "Allowed" }, Severity.Error)));
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        });
        // Exercise the CLI's executable-config transport without restoring a test-only analyzer package.
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), $$$""""
            System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("SHARPLINT_CONFIG_OUTPUT")!, """
            {{{json}}}
            """);
            """");
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"),
            "class Allowed {} class Forbidden : Allowed { void M(int x, int y) {} }");
        var arguments = new List<string> { "check", "--path", _workDir, "--format", "json" };
        if (noDaemon) arguments.Add("--no-daemon");
        var (exit, stdout, stderr) = await RunCliAsync(arguments.ToArray());
        Assert.True(exit == 1, stdout + stderr);
        using var document = JsonDocument.Parse(stdout);
        var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Contains(diagnostics, d => d.GetProperty("ruleId").GetString() == "ACME001"
            && d.GetProperty("module").GetString() == "acme.security"
            && d.GetProperty("category").GetString() == "Acme concern");
        Assert.Contains(diagnostics, d => d.GetProperty("ruleId").GetString() == "design.maximum-parameters"
            && d.GetProperty("module").GetString() == "design");
    }
}
