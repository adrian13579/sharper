using System.Diagnostics;
using System.Text.Json;

namespace SharpLint.Tests.Cli;

public sealed class CheckCommandFunctionalTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _repoRoot;
    private readonly string _cliDll;
    private readonly string _nugetCache;

    public CheckCommandFunctionalTests()
    {
        _repoRoot = LocateRepoRoot();
        _cliDll = Path.Combine(_repoRoot, "src", "SharpLint.Cli", "bin", "Debug", "net10.0", "sharplint.dll");
        _workDir = Path.Combine(Path.GetTempPath(), $"sharplint-cli-{Guid.NewGuid():N}");
        _nugetCache = Path.Combine(_workDir, ".nuget-cache");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task Check_emits_json_diagnostics_and_exits_nonzero_on_error()
    {
        WriteNuGetConfig();
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), """
            #:package SharpLint.Configuration@1.0.0-dev

            using SharpLint.Configuration;
            using SharpLint.Core.Naming;

            Lint.Configure(config =>
            {
                config.Naming(n =>
                {
                    n.Classes().RequireConvention(NamingConvention.PascalCase).AsError();
                    n.Interfaces().RequirePrefix("I");
                });
            }).Apply();
            """);
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), """
            public interface Foo { }
            public class lowerCase { }
            """);

        var (exit, stdout, stderr) = await RunCliAsync("check", "--path", _workDir, "--format", "json");

        Assert.True(exit == 1, $"Expected exit 1, got {exit}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var diags = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.Contains(diags, d =>
            d.GetProperty("ruleId").GetString() == "naming.class.convention"
            && d.GetProperty("severity").GetString() == "error"
            && d.GetProperty("message").GetString()!.Contains("lowerCase", StringComparison.Ordinal));

        Assert.Contains(diags, d =>
            d.GetProperty("ruleId").GetString() == "naming.interface.prefix"
            && d.GetProperty("severity").GetString() == "warning");
    }

    [Fact]
    public async Task Check_exits_zero_when_no_violations()
    {
        WriteNuGetConfig();
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), """
            #:package SharpLint.Configuration@1.0.0-dev

            using SharpLint.Configuration;
            using SharpLint.Core.Naming;

            Lint.Configure(config =>
            {
                config.Naming(n =>
                {
                    n.Classes().RequireConvention(NamingConvention.PascalCase).AsError();
                });
            }).Apply();
            """);
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "public class GoodName { }");

        var (exit, stdout, stderr) = await RunCliAsync("check", "--path", _workDir, "--format", "json");

        Assert.True(exit == 0, $"Expected exit 0, got {exit}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        using var doc = JsonDocument.Parse(stdout);
        Assert.Empty(doc.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Check_preserves_same_verb_composition_through_executable_config()
    {
        WriteNuGetConfig();
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), """
            #:package SharpLint.Configuration@1.0.0-dev
            using SharpLint.Configuration;
            Lint.Configure(c => c.Naming(n =>
            {
                n.Interfaces().RequirePrefix("X");
                n.Interfaces().RequirePrefix("I").AsError().ForbidPrefix("IInternal");
            })).Apply();
            """);
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"),
            "interface Good {} interface IInternalBad {} interface IGood {}");

        var (exit, stdout, stderr) = await RunCliAsync("check", "--path", _workDir, "--format", "json");
        Assert.True(exit == 1, $"Expected exit 1, got {exit}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal("Interface 'Good' must start with 'I'.", diagnostics[0].GetProperty("message").GetString());
        Assert.Equal("error", diagnostics[0].GetProperty("severity").GetString());
        Assert.Equal("Interface 'IInternalBad' must not start with 'IInternal'.", diagnostics[1].GetProperty("message").GetString());
        Assert.Equal("warning", diagnostics[1].GetProperty("severity").GetString());
        Assert.All(diagnostics, d => Assert.Equal("naming.interface.prefix", d.GetProperty("ruleId").GetString()));
    }

    [Fact]
    public async Task Init_creates_a_runnable_recommended_config_in_the_working_directory()
    {
        WriteNuGetConfig();
        var (exit, stdout, stderr) = await RunCliAsync("init");
        Assert.True(exit == 0, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("Created sharplint.config.cs", stdout);
        var configPath = Path.Combine(_workDir, "sharplint.config.cs");
        var config = File.ReadAllText(configPath);
        Assert.Contains("#:package SharpLint.Configuration@1.0.0-dev", config);
        Assert.Contains("config.UsePreset(Preset.Recommended)", config);
        Assert.Contains(".Apply()", config);

        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "class lowerCase {}");
        var check = await RunCliAsync("check", "--format", "json");
        Assert.True(check.ExitCode == 0, $"stdout:\n{check.Stdout}\nstderr:\n{check.Stderr}");
        using var document = JsonDocument.Parse(check.Stdout);
        var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("naming.class.convention", diagnostic.GetProperty("ruleId").GetString());
        Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
        Assert.Contains("lowerCase", diagnostic.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Init_refuses_to_overwrite_an_existing_config()
    {
        var path = Path.Combine(_workDir, "sharplint.config.cs");
        const string original = "// Existing user configuration\n";
        File.WriteAllText(path, original);

        var (exit, stdout, _) = await RunCliAsync("init");

        Assert.Equal(1, exit);
        Assert.Contains("sharplint.config.cs already exists.", stdout);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task Check_without_config_uses_current_directory_and_reports_no_rules()
    {
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "class lowerCase {}");
        var (exit, stdout, stderr) = await RunCliAsync("check", "--format", "JSON");

        Assert.True(exit == 0, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(1, document.RootElement.GetProperty("filesAnalyzed").GetInt32());
        Assert.True(document.RootElement.GetProperty("elapsedMs").GetDouble() >= 0);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.False(File.Exists(Path.Combine(_workDir, "sharplint.config.cs")));
    }

    [Theory]
    [InlineData("AsWarning", "warning", 0)]
    [InlineData("AsInfo", "info", 0)]
    [InlineData("AsError", "error", 1)]
    [InlineData("Disabled", null, 0)]
    public async Task Check_json_preserves_severity_location_and_exit_status(string modifier, string? severity, int expectedExit)
    {
        WriteConfig($"n.Classes().RequirePrefix(\"Expected\").{modifier}();");
        var sourcePath = Path.Combine(_workDir, "Sample.cs");
        File.WriteAllText(sourcePath, "// heading\n  class Other {}\n");

        var (exit, stdout, stderr) = await RunCliAsync("check", "--format", "json");

        Assert.True(exit == expectedExit, $"Expected {expectedExit}, got {exit}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(2, document.RootElement.GetProperty("filesAnalyzed").GetInt32());
        var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        if (severity is null)
        {
            Assert.Empty(diagnostics);
            return;
        }
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("naming.class.prefix", diagnostic.GetProperty("ruleId").GetString());
        Assert.Equal(severity, diagnostic.GetProperty("severity").GetString());
        Assert.Equal("Class 'Other' must start with 'Expected'.", diagnostic.GetProperty("message").GetString());
        Assert.Equal(sourcePath, diagnostic.GetProperty("file").GetString());
        Assert.Equal(2, diagnostic.GetProperty("line").GetInt32());
        Assert.Equal(9, diagnostic.GetProperty("column").GetInt32());
        Assert.Equal(JsonValueKind.Null, diagnostic.GetProperty("suggestion").ValueKind);
    }

    [Theory]
    [InlineData("AsWarning", "warning", 0)]
    [InlineData("AsInfo", "info", 0)]
    [InlineData("AsError", "error", 1)]
    public async Task Check_default_pretty_output_renders_severity_pattern_and_location(string modifier, string severity, int expectedExit)
    {
        WriteConfig($"n.Classes().RequireRegex(\"[A-Z]+\").{modifier}();");
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "class lower {}");

        var (exit, stdout, stderr) = await RunCliAsync("check");

        Assert.True(exit == expectedExit, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("Files analyzed:", stdout);
        Assert.Contains("Elapsed:", stdout);
        Assert.Contains($"{severity} naming.class.regex", stdout);
        Assert.Contains("Class 'lower' must match pattern '[A-Z]+'.", stdout);
        Assert.Contains("Sample.cs:1:7", stdout);
        Assert.Contains("1 diagnostic(s).", stdout);
    }

    [Fact]
    public async Task Check_pretty_output_reports_a_clean_directory()
    {
        var (exit, stdout, stderr) = await RunCliAsync("check");

        Assert.True(exit == 0, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("Files analyzed: 0", stdout);
        Assert.Contains("No issues found.", stdout);
    }

    [Fact]
    public async Task Check_relative_path_uses_the_target_config_and_ignores_build_directories()
    {
        var target = Path.Combine(_workDir, "nested project");
        Directory.CreateDirectory(target);
        WriteConfig("n.Classes().RequirePrefix(\"Expected\").AsError();");
        File.Move(Path.Combine(_workDir, "sharplint.config.cs"), Path.Combine(target, "sharplint.config.cs"));
        foreach (var directory in new[] { "bin", "obj" })
        {
            Directory.CreateDirectory(Path.Combine(target, directory));
            File.WriteAllText(Path.Combine(target, directory, "Generated.cs"), "class Wrong {}");
        }
        File.WriteAllText(Path.Combine(target, "Sample.cs"), "class Other {}");
        File.WriteAllText(Path.Combine(_workDir, "Outside.cs"), "class Outside {}");

        var (exit, stdout, stderr) = await RunCliAsync("check", "--path", "nested project", "--format", "json");

        Assert.True(exit == 1, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(2, document.RootElement.GetProperty("filesAnalyzed").GetInt32());
        var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(Path.Combine(target, "Sample.cs"), diagnostic.GetProperty("file").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Check_config_failure_exits_nonzero_and_reports_the_cause()
    {
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), """
            using System;
            Console.Error.WriteLine("configuration failure marker");
            return 7;
            """);
        var (exit, stdout, stderr) = await RunCliAsync("check", "--format", "json");

        Assert.NotEqual(0, exit);
        Assert.Contains("configuration failure marker", stdout + stderr);
        Assert.Contains("exit code 7", stdout + stderr);
    }

    [Fact]
    public async Task Check_missing_directory_exits_nonzero()
    {
        var (exit, stdout, stderr) = await RunCliAsync("check", "--path", "does-not-exist");

        Assert.NotEqual(0, exit);
        Assert.Contains("does-not-exist", stdout + stderr);
    }

    [Theory]
    [InlineData("unknown-command")]
    [InlineData("check", "--path")]
    [InlineData("check", "--format")]
    public async Task Invalid_invocations_exit_nonzero(params string[] args)
    {
        var (exit, stdout, stderr) = await RunCliAsync(args);

        Assert.NotEqual(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(stdout + stderr));
    }

    [Fact]
    public async Task Help_lists_available_commands_without_creating_a_config()
    {
        var (exit, stdout, stderr) = await RunCliAsync("--help");

        Assert.True(exit == 0, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("check", stdout);
        Assert.Contains("init", stdout);
        Assert.False(File.Exists(Path.Combine(_workDir, "sharplint.config.cs")));
    }

    private void WriteConfig(string rules)
    {
        WriteNuGetConfig();
        File.WriteAllText(Path.Combine(_workDir, "sharplint.config.cs"), $$"""
            #:package SharpLint.Configuration@1.0.0-dev
            using SharpLint.Configuration;
            Lint.Configure(c => c.Naming(n => { {{rules}} })).Apply();
            """);
    }

    private void WriteNuGetConfig()
    {
        var packagesPath = Path.Combine(_repoRoot, "packages");
        File.WriteAllText(Path.Combine(_workDir, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{_nugetCache}" />
              </config>
              <packageSources>
                <clear />
                <add key="local-dev" value="{packagesPath}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
              </packageSources>
            </configuration>
            """);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(params string[] args)
    {
        if (!File.Exists(_cliDll))
            throw new InvalidOperationException($"CLI binary missing at {_cliDll}. Build the solution first.");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(_cliDll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["NUGET_PACKAGES"] = _nugetCache;
        psi.Environment["NO_COLOR"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the CLI process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, PlainText(await stdoutTask), PlainText(await stderrTask));
    }

    private static string PlainText(string output) =>
        System.Text.RegularExpressions.Regex.Replace(output, "\u001b\\[[0-9;]*m", string.Empty);

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SharpLint.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
    }
}
