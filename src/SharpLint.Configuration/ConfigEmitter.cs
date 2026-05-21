using System.Text.Json;
using System.Text.Json.Serialization;
using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

public static class ConfigEmitter
{
    internal const string OutputPathEnvVar = "SHARPLINT_CONFIG_OUTPUT";

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
