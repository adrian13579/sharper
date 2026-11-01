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
    /// <summary>External Roslyn modules referenced by executable configuration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AnalyzerModuleReference>? Modules { get; init; }

    /// <summary>Resolves configured external modules alongside built-ins and optional host registrations.</summary>
    public RuleRegistry CreateRegistry(RuleRegistry? hostRegistry = null) => new(
        (hostRegistry?.Modules.Where(m => !SharpLint.Core.Rules.Rules.All.Contains(m)) ?? [])
        .Concat(Modules?.Select(m => m.Load()) ?? []));

    /// <summary>Rule configurations for built-in and external modules.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ConfiguredRule>? Rules { get; init; }
}
