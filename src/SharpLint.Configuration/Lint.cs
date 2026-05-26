namespace SharpLint.Configuration;

/// <summary>
/// Entry point for a <c>sharplint.config.cs</c> file. Begin every config with
/// <see cref="Configure"/>, then call <see cref="ConfigEmitter.Apply"/> on the result.
/// </summary>
public static class Lint
{
    /// <summary>
    /// Builds the SharpLint configuration. Use the supplied <see cref="ConfigBuilder"/> to apply a
    /// preset and declare rules, for example <c>config =&gt; config.UsePreset(Preset.Recommended)</c>.
    /// </summary>
    /// <param name="configure">Callback that declares rules on the builder.</param>
    /// <returns>The assembled configuration; call <c>.Apply()</c> on it to emit the config.</returns>
    public static SharpLintConfig Configure(Action<ConfigBuilder> configure)
    {
        var builder = new ConfigBuilder();
        configure(builder);
        return builder.Build();
    }
}
