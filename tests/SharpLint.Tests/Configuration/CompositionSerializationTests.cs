using System.Text.Json;
using System.Text.Json.Serialization;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Naming;

namespace SharpLint.Tests.Configuration;

public sealed class CompositionSerializationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharplint-composition-{Guid.NewGuid():N}");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public CompositionSerializationTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task<AnalysisResult> Analyze(SharpLintConfig config)
    {
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), "class Other {} class IBad {} class IGood {}");
        return await new Analyzer().AnalyzeAsync(new AnalysisContext { WorkingDirectory = _directory, NamingRules = config.NamingRules });
    }

    [Fact]
    public async Task Serialized_configuration_preserves_composition_and_separate_selector_precedence()
    {
        var config = Lint.Configure(c => c.Naming(n =>
        {
            // Create selectors before adding any rules: they must still get distinct IDs.
            var earlier = n.Classes();
            var later = n.Classes();
            earlier.RequirePrefix("X").ForbidPrefix("IGood");
            later.RequirePrefix("I").ForbidPrefix("IBad");
        }));
        Assert.Equal(0, config.NamingRules[0].ChainId);
        Assert.Equal(0, config.NamingRules[1].ChainId);
        Assert.Equal(2, config.NamingRules[2].ChainId);
        Assert.Equal(2, config.NamingRules[3].ChainId);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var loaded = JsonSerializer.Deserialize<SharpLintConfig>(json, JsonOptions)!;
        var result = await Analyze(loaded);
        Assert.Equal(new[] { "Class 'Other' must start with 'I'.", "Class 'IBad' must not start with 'IBad'." },
            result.Diagnostics.Select(d => d.Message));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"chainId\":null")]
    public async Task Legacy_rules_without_chain_ids_remain_standalone(string metadata)
    {
        var json = $$"""
            {"namingRules":[
              {"kind":"Class","verb":"Prefix","direction":"Require","values":["X"]{{metadata}}},
              {"kind":"Class","verb":"Prefix","direction":"Forbid","values":["IBad"]{{metadata}}}
            ]}
            """;
        var config = JsonSerializer.Deserialize<SharpLintConfig>(json, JsonOptions)!;
        var result = await Analyze(config);
        Assert.Equal("Class 'IBad' must not start with 'IBad'.", Assert.Single(result.Diagnostics).Message);
    }
}
