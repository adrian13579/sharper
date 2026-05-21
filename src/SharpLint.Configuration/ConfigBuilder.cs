using SharpLint.Configuration.Dsl;
using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

public sealed class ConfigBuilder
{
    private string _preset = Preset.Recommended;
    private readonly List<NamingRule> _namingRules = [];

    public ConfigBuilder UsePreset(string preset)
    {
        _preset = preset;
        return this;
    }

    public ConfigBuilder Naming(Action<NamingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NamingBuilder(_namingRules));
        return this;
    }

    internal SharpLintConfig Build() => new(_preset, _namingRules);
}
