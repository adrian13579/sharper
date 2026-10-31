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

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the CLI process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

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
