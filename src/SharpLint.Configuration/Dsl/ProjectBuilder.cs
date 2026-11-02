namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in project requirements.</summary>
public sealed class ProjectBuilder
{
    private readonly ConfigBuilder _configuration;

    internal ProjectBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets literal nullable declarations in projects.</summary>
    public NullableProjectRuleChain Nullable() => new(_configuration);

    /// <summary>Targets literal target framework declarations in projects.</summary>
    public TargetFrameworkRuleChain TargetFrameworks() => new(_configuration);
}
