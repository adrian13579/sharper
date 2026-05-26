using SharpLint.Configuration.Dsl;
using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

/// <summary>
/// Accumulates the rules that make up a configuration. Supplied to the
/// <see cref="Lint.Configure"/> callback; chain its methods to declare rules.
/// </summary>
public sealed class ConfigBuilder
{
    private readonly List<NamingRule> _namingRules = [];

    /// <summary>
    /// Applies a bundle of rules, such as <see cref="Preset.Recommended"/>, as a starting point.
    /// Rules declared afterwards add to (and can override) the preset.
    /// </summary>
    /// <param name="preset">A preset from <see cref="Preset"/>, or your own rule bundle.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ConfigBuilder UsePreset(Action<ConfigBuilder> preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset(this);
        return this;
    }

    /// <summary>
    /// Declares naming rules. Use the supplied <see cref="NamingBuilder"/> to pick a symbol kind
    /// (e.g. <c>n.Classes()</c>) and attach conventions, prefixes, suffixes, or patterns.
    /// </summary>
    /// <param name="configure">Callback that declares naming rules.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ConfigBuilder Naming(Action<NamingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NamingBuilder(_namingRules));
        return this;
    }

    internal SharpLintConfig Build() => new(_namingRules);
}
