using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

/// <summary>
/// The assembled, immutable configuration produced by <see cref="Lint.Configure"/>.
/// </summary>
/// <param name="NamingRules">The naming rules declared in the config, in declaration order.</param>
public sealed record SharpLintConfig(IReadOnlyList<NamingRule> NamingRules);
