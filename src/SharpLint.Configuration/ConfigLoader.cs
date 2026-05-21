using System.Diagnostics;
using System.Text.Json;

namespace SharpLint.Configuration;

public static class ConfigLoader
{
    public const string DefaultFileName = "sharplint.config.cs";

    public static async Task<SharpLintConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"sharplint-config-{Guid.NewGuid():N}.json");

        try
        {
            await RunConfigScriptAsync(path, outputPath, cancellationToken).ConfigureAwait(false);

            var json = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SharpLintConfig);

            return config
                ?? throw new InvalidOperationException("Config file produced no output. Did you forget to call `.Apply()`?");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    public static SharpLintConfig Default() => new(Preset.Recommended, []);

    private static async Task RunConfigScriptAsync(string scriptPath, string outputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(scriptPath)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.Environment[ConfigEmitter.OutputPathEnvVar] = outputPath;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start `dotnet run` for the config file.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Config file failed to run (exit code {process.ExitCode}).\n" +
                $"stdout:\n{stdout}\nstderr:\n{stderr}");
        }
    }
}
