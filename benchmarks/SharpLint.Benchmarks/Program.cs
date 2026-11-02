using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpLint.Analysis;

// Uses only a private copy of the integration fixture. Never edit a caller's project for timing.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var server = Environment.GetEnvironmentVariable("SHARPLINT_TEST_ROSLYN")
    ?? throw new ArgumentException("Set SHARPLINT_TEST_ROSLYN to the installed official server executable.");
var samples = args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 20;
if (samples < 20) throw new ArgumentException("Use at least 20 warm samples per scenario.");
var fixture = Path.Combine(Path.GetTempPath(), "sharplint-benchmark-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var source = Path.Combine(repository, "tests/SharpLint.Tests/Fixtures/Lsp/TestSolution");
var results = new Dictionary<string, List<Measurement>>(StringComparer.Ordinal);
try
{
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
        .Where(f => !Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")))
    {
        var target = Path.Combine(fixture, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target);
    }
    await Run("restore", "TestSolution.slnx", "-m:1", "--disable-build-servers");
    await Run("build", "CustomAnalyzer/CustomAnalyzer.csproj", "--no-restore", "-m:1", "--disable-build-servers");
    for (var sample = 0; sample < 5; sample++)
    {
        await using var cold = Create();
        var watch = Stopwatch.StartNew();
        var status = await cold.StatusAsync();
        Add("initialLoad", new(watch.Elapsed.TotalMilliseconds, status.Available && status.WorkspaceState == "ready" ? "completed" : "failed", 1));
        if (!status.Available) throw new IOException(status.LastError);
    }
    await using var warm = Create();
    await Check(warm, "Domain/Widget.cs"); // Warmup is excluded from reported samples.
    var widget = Path.Combine(fixture, "Domain/Widget.cs");
    var original = File.ReadAllText(widget);
    var project = Path.Combine(fixture, "Tests/Tests.csproj");
    var projectText = File.ReadAllText(project);
    for (var sample = 0; sample < samples; sample++)
    {
        Add("unchangedFile", await Measure("Domain/Widget.cs"));
        File.WriteAllText(widget, original + (sample % 2 == 0 ? "\nclass Broken {" : "\nclass Broken {}"));
        Add("syntaxEdit", await Measure("Domain/Widget.cs"));
        File.WriteAllText(widget, original.Replace("int Value => missing", sample % 2 == 0 ? "string Value => \"value\"" : "int Value => 42", StringComparison.Ordinal));
        Add("semanticEdit", await Measure("Application/Consumer.cs"));
        File.AppendAllText(widget, $"\n// batch {sample}\n");
        File.AppendAllText(Path.Combine(fixture, "Application/Consumer.cs"), $"\n// batch {sample}\n");
        File.AppendAllText(Path.Combine(fixture, "Tests/Smoke.cs"), $"\n// batch {sample}\n");
        Add("multipleChangedFiles", await Measure(null));
        Add("repeatedWarmCall", await Measure("Domain/Widget.cs"));
        File.WriteAllText(project, sample % 2 == 0
            ? projectText.Replace("</ItemGroup>", "<ProjectReference Include=\"../Domain/Domain.csproj\" /></ItemGroup>", StringComparison.Ordinal) : projectText);
        Add("projectReferenceReload", await Measure("Application/Consumer.cs"));
    }
    var output = new
    {
        recordedAtUtc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription,
        sdk = "10.0.202 (repository global.json; verify with dotnet --info)", os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(), logicalProcessors = Environment.ProcessorCount,
        cpu = File.Exists("/proc/cpuinfo") ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)) : null,
        server, samplesPerWarmScenario = samples, coldSamples = 5,
        unit = "milliseconds", includes = "Full disk reconciliation and bounded incomplete-result retries; project changes include session reload. No build or restore in timed checks.",
        scenarios = results.ToDictionary(p => p.Key, p => new
        {
            samples = p.Value, medianMs = Percentile(p.Value, .5), p95Ms = Percentile(p.Value, .95),
            incompleteOrFailed = p.Value.Count(m => m.Status != "completed"),
        }),
    };
    var destination = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(repository, "benchmarks/lsp-results.json");
    await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(output, new JsonSerializerOptions(DiagnosticJson.Options) { WriteIndented = true }));
    Console.WriteLine(destination);

    async Task<Measurement> Measure(string? path)
    {
        var watch = Stopwatch.StartNew();
        var (response, attempts) = await Check(warm, path);
        return new(watch.Elapsed.TotalMilliseconds, response.Status, attempts);
    }
}
finally { Directory.Delete(fixture, true); }

DiagnosticService Create() => new(new DiagnosticServiceOptions { RootPath = fixture, ServerPath = server });
void Add(string scenario, Measurement measurement)
{
    if (!results.TryGetValue(scenario, out var values)) results.Add(scenario, values = []);
    values.Add(measurement);
    Console.Error.WriteLine($"{scenario}: {measurement.DurationMs:F1} ms ({measurement.Status})");
}
static double Percentile(List<Measurement> measurements, double percentile)
{
    var sorted = measurements.Select(m => m.DurationMs).Order().ToArray();
    // Nearest-rank P95; median averages the middle pair for even sample counts.
    return percentile == .5 && sorted.Length % 2 == 0 ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
        : sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
}
static async Task<(DiagnosticResponse Response, int Attempts)> Check(DiagnosticService service, string? path)
{
    DiagnosticResponse? response = null;
    var attempt = 0;
    while (attempt++ < 3)
    {
        response = await service.CheckAsync(path);
        if (response.Complete || response.Status != "incomplete") return (response, attempt);
        await Task.Delay(150);
    }
    return (response!, 3);
}
async Task Run(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
    var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { process.Kill(entireProcessTree: true); throw; }
    if (process.ExitCode != 0) throw new IOException(await stdout + await stderr);
    await stdout;
    await stderr;
}
internal sealed record Measurement(double DurationMs, string Status, int Attempts);
