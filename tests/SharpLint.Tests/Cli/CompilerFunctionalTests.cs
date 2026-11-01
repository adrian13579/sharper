using System.Diagnostics;
using System.Text.Json;

namespace SharpLint.Tests.Cli;

public sealed partial class CheckCommandFunctionalTests
{
    private async Task WriteProjectAsync(string options = "")
    {
        var path = Path.Combine(_workDir, "Example.csproj");
        File.WriteAllText(path, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                {{options}}
              </PropertyGroup>
            </Project>
            """);
        var start = new ProcessStartInfo("dotnet")
        { WorkingDirectory = _workDir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "restore", path, "--ignore-failed-sources", "-p:NuGetAudit=false" }) start.ArgumentList.Add(arg);
        start.Environment["NUGET_PACKAGES"] = _nugetCache;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    [Fact]
    public async Task Compiler_and_naming_errors_serialize_together_in_both_execution_modes()
    {
        WriteConfig("n.Classes().RequirePrefix(\"Expected\").AsError();");
        await WriteProjectAsync();
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "class Wrong { int M() => missing; }");
        var incremental = await CheckWithStats();
        var diagnostics = incremental.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Equal("completed", incremental.GetProperty("status").GetString());
        Assert.Empty(incremental.GetProperty("failures").EnumerateArray());
        var compiler = Assert.Single(diagnostics, d => d.GetProperty("origin").GetString() == "compiler");
        Assert.Equal("CS0103", compiler.GetProperty("ruleId").GetString());
        Assert.Equal("error", compiler.GetProperty("severity").GetString());
        Assert.Equal(1, compiler.GetProperty("line").GetInt32());
        Assert.Equal(26, compiler.GetProperty("column").GetInt32());
        Assert.Equal(7, compiler.GetProperty("spanLength").GetInt32());
        Assert.Contains("Example.csproj", compiler.GetProperty("project").GetString());
        Assert.Contains(diagnostics, d => d.GetProperty("origin").GetString() == "sharplint"
            && d.GetProperty("ruleId").GetString() == "naming.class.prefix");
        await AssertMatchesFull(incremental);
        var warm = await CheckWithStats();
        Assert.Equal(1, warm.GetProperty("statistics").GetProperty("projectsReused").GetInt32());
        var pretty = await RunCliAsync("check");
        Assert.Equal(1, pretty.ExitCode);
        Assert.Contains("CS0103 [Compiler]", pretty.Stdout);
        Assert.Contains("naming.class.prefix [SharpLint]", pretty.Stdout);
    }

    [Fact]
    public async Task Compiler_warnings_respect_project_policy_and_locationless_errors_are_structured()
    {
        await WriteProjectAsync();
        File.WriteAllText(Path.Combine(_workDir, "Sample.cs"), "class Good { void M() { int unused; } }");
        var warning = await RunCliAsync("check", "--format", "json");
        Assert.True(warning.ExitCode == 0, warning.Stdout + warning.Stderr);
        using var warnings = JsonDocument.Parse(warning.Stdout);
        Assert.Equal("warning", Assert.Single(warnings.RootElement.GetProperty("diagnostics").EnumerateArray()).GetProperty("severity").GetString());
        await WriteProjectAsync("<TreatWarningsAsErrors>true</TreatWarningsAsErrors><OutputType>Exe</OutputType>");
        foreach (var mode in new[] { Array.Empty<string>(), new[] { "--no-daemon" } })
        {
            var errors = await RunCliAsync(["check", "--format", "json", .. mode]);
            Assert.True(errors.ExitCode == 1, errors.Stdout + errors.Stderr);
            using var json = JsonDocument.Parse(errors.Stdout);
            var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
            Assert.Equal("error", Assert.Single(diagnostics, d => d.GetProperty("ruleId").GetString() == "CS0168").GetProperty("severity").GetString());
            var noLocation = Assert.Single(diagnostics, d => d.GetProperty("ruleId").GetString() == "CS5001");
            Assert.Equal(JsonValueKind.Null, noLocation.GetProperty("file").ValueKind);
            Assert.Equal(JsonValueKind.Null, noLocation.GetProperty("line").ValueKind);
            Assert.Equal(JsonValueKind.Null, noLocation.GetProperty("column").ValueKind);
        }
        File.WriteAllText(Path.Combine(_workDir, "Example.csproj"), "invalid XML");
        var failed = await RunCliAsync("check", "--format", "json");
        Assert.Equal(2, failed.ExitCode);
        Assert.Empty(failed.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(failed.Stderr));
    }
}
