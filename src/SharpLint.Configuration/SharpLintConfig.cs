using System.Text.Json.Serialization;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;

namespace SharpLint.Configuration;

/// <summary>
/// The assembled, immutable configuration produced by <see cref="Lint.Configure"/>.
/// </summary>
/// <param name="NamingRules">The naming rules declared in the config, in declaration order.</param>
public sealed record SharpLintConfig(IReadOnlyList<NamingRule> NamingRules)
{
    /// <summary>Module rule configurations; analyzers must be supplied separately by the host.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ConfiguredRule>? Rules { get; init; }
}
