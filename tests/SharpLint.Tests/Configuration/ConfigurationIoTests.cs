using System.Text.Json;
using SharpLint.Configuration;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Tests.Configuration;

// Apply writes process-wide environment/console state; keep this collection isolated.
[CollectionDefinition("Configuration IO", DisableParallelization = true)]
public sealed class ConfigurationIoCollection;

[Collection("Configuration IO")]
public sealed class ConfigurationIoTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharplint-config-tests-{Guid.NewGuid():N}");
    private const string OutputVariable = "SHARPLINT_CONFIG_OUTPUT";

    public ConfigurationIoTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Apply_without_output_path_writes_json_to_stdout(string? outputPath)
    {
        var previousPath = Environment.GetEnvironmentVariable(OutputVariable);
        var previousOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable(OutputVariable, outputPath);
            Console.SetOut(writer);
            ExampleConfig().Apply();
        }
        finally
        {
            Console.SetOut(previousOut);
            Environment.SetEnvironmentVariable(OutputVariable, previousPath);
        }
        AssertConfigJson(writer.ToString());
    }

    [Fact]
    public void Apply_writes_json_to_the_requested_file_without_stdout()
    {
        var path = Path.Combine(_directory, "output.json");
        var previousPath = Environment.GetEnvironmentVariable(OutputVariable);
        var previousOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable(OutputVariable, path);
            Console.SetOut(writer);
            ExampleConfig().Apply();
        }
        finally
        {
            Console.SetOut(previousOut);
            Environment.SetEnvironmentVariable(OutputVariable, previousPath);
        }
        AssertConfigJson(File.ReadAllText(path));
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void Apply_rejects_a_null_config() =>
        Assert.Throws<ArgumentNullException>(() => ConfigEmitter.Apply(null!));

    [Fact]
    public void Default_configuration_has_no_rules() => Assert.Empty(ConfigLoader.Default().NamingRules);

    private static SharpLintConfig ExampleConfig() => Lint.Configure(c => c.Naming(n =>
        n.Classes(p => p.Public().Abstract()).RequirePrefix("Base", "Abstract").AsError().ExceptWithAttribute<ObsoleteAttribute>())
        .Rule("example.limit", JsonSerializer.SerializeToElement(new { maximum = 5 }), Severity.Info));

    private static void AssertConfigJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rule = Assert.Single(document.RootElement.GetProperty("namingRules").EnumerateArray());
        Assert.Equal(0, rule.GetProperty("chainId").GetInt32());
        Assert.Equal("Class", rule.GetProperty("kind").GetString());
        Assert.Equal("Public, Abstract", rule.GetProperty("predicates").GetString());
        Assert.Equal("Prefix", rule.GetProperty("verb").GetString());
        Assert.Equal("Require", rule.GetProperty("direction").GetString());
        Assert.Equal("Error", rule.GetProperty("severity").GetString());
        Assert.Equal(new[] { "Base", "Abstract" }, rule.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("System.ObsoleteAttribute", Assert.Single(rule.GetProperty("exemptAttributes").EnumerateArray()).GetString());
        var moduleRule = Assert.Single(document.RootElement.GetProperty("rules").EnumerateArray());
        Assert.Equal("example.limit", moduleRule.GetProperty("ruleId").GetString());
        Assert.Equal("Info", moduleRule.GetProperty("severity").GetString());
        Assert.Equal(5, moduleRule.GetProperty("options").GetProperty("maximum").GetInt32());
    }

    private string WriteScript(string json, int exitCode = 0)
    {
        // No package dependencies: the child emits the wire format while LoadAsync itself
        // executes in the test host, allowing Stryker to instrument the loader.
        var path = Path.Combine(_directory, "sample.config.cs");
        File.WriteAllText(path, $$"""
            using System;
            using System.IO;
            using System.Threading;
            var output = Environment.GetEnvironmentVariable("SHARPLINT_CONFIG_OUTPUT")!;
            File.WriteAllText("observed-output.txt", output);
            Console.WriteLine("config stdout marker");
            Console.Error.WriteLine("config stderr marker");
            Thread.Sleep(100);
            File.WriteAllText(output, {{JsonSerializer.Serialize(json)}});
            return {{exitCode}};
            """);
        return path;
    }

    private void AssertTemporaryOutputWasCleanedUp()
    {
        var path = File.ReadAllText(Path.Combine(_directory, "observed-output.txt"));
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(path));
        Assert.StartsWith("sharplint-config-", Path.GetFileName(path));
        Assert.EndsWith(".json", path);
        Assert.False(File.Exists(path), $"Temporary config output was not cleaned up: {path}");
    }

    [Fact]
    public async Task Load_executes_in_script_directory_deserializes_rules_and_cleans_output()
    {
        var path = WriteScript("""
            {"namingRules":[{"chainId":4,"kind":"Class","predicates":"Public, Abstract","verb":"Prefix","direction":"Require","values":["Base","Abstract"],"severity":"Error","exemptAttributes":["System.ObsoleteAttribute"]}]}
            """);
        var config = await ConfigLoader.LoadAsync(path);
        var rule = Assert.Single(config.NamingRules);
        Assert.Equal(4, rule.ChainId);
        Assert.Equal(SymbolKind.Class, rule.Kind);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Abstract, rule.Predicates);
        Assert.Equal(VerbKind.Prefix, rule.Verb);
        Assert.Equal(Direction.Require, rule.Direction);
        Assert.Equal(new[] { "Base", "Abstract" }, rule.Values);
        Assert.Equal(Severity.Error, rule.Severity);
        Assert.Equal(new[] { "System.ObsoleteAttribute" }, rule.ExemptAttributes);
        AssertTemporaryOutputWasCleanedUp();
    }

    [Fact]
    public async Task Load_preserves_module_options_and_severity()
    {
        var config = await ConfigLoader.LoadAsync(WriteScript("""
            {"namingRules":[],"rules":[{"ruleId":"example.limit","options":{"maximum":5},"severity":"Error"}]}
            """));
        var rule = Assert.Single(config.Rules!);
        Assert.Equal("example.limit", rule.RuleId);
        Assert.Equal(Severity.Error, rule.Severity);
        Assert.Equal(5, rule.Options.GetProperty("maximum").GetInt32());
        AssertTemporaryOutputWasCleanedUp();
    }

    [Fact]
    public async Task Load_reports_exit_code_stdout_and_stderr_and_cleans_failed_output()
    {
        var path = WriteScript("{}", 7);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigLoader.LoadAsync(path));
        Assert.Contains("Config file failed to run (exit code 7).", error.Message);
        Assert.Contains("stdout:\nconfig stdout marker", error.Message);
        Assert.Contains("stderr:\nconfig stderr marker", error.Message);
        AssertTemporaryOutputWasCleanedUp();
    }

    [Fact]
    public async Task Load_rejects_null_configuration_and_cleans_output()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigLoader.LoadAsync(WriteScript("null")));
        Assert.Equal("Config file produced no output. Did you forget to call `.Apply()`?", error.Message);
        AssertTemporaryOutputWasCleanedUp();
    }

    [Fact]
    public async Task Load_rejects_malformed_json_and_cleans_output()
    {
        await Assert.ThrowsAsync<JsonException>(() => ConfigLoader.LoadAsync(WriteScript("invalid json")));
        AssertTemporaryOutputWasCleanedUp();
    }
}
