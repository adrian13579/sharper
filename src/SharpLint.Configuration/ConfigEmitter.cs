using System.Text.Json;
using System.Text.Json.Serialization;
using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

/// <summary>
/// Extension that emits a built configuration so the SharpLint CLI can read it.
/// </summary>
public static class ConfigEmitter
{
    internal const string OutputPathEnvVar = "SHARPLINT_CONFIG_OUTPUT";

    /// <summary>
    /// Emits the configuration. Call this at the end of <c>sharplint.config.cs</c>, after
    /// <see cref="Lint.Configure"/>. Without it the CLI sees no config and the file has no effect.
    /// </summary>
    /// <param name="config">The configuration returned by <see cref="Lint.Configure"/>.</param>
    public static void Apply(this SharpLintConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.SharpLintConfig);
        var outputPath = Environment.GetEnvironmentVariable(OutputPathEnvVar);

        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine(json);
            return;
        }

        File.WriteAllText(outputPath, json);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SharpLintConfig))]
[JsonSerializable(typeof(NamingRule))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
