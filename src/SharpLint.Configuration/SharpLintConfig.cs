using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

public sealed record SharpLintConfig(
    string Preset,
    IReadOnlyList<NamingRule> NamingRules);
