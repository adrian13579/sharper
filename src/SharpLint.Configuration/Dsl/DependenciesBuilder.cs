namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in dependencies requirements.</summary>
public sealed class DependenciesBuilder
{
    private readonly ConfigBuilder _configuration;

    internal DependenciesBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets explicit using directives.</summary>
    public UsingDependencyRuleChain Usings() => new(_configuration);

    /// <summary>Targets literal package declarations.</summary>
    public PackageDependencyRuleChain Packages() => new(_configuration);
}
