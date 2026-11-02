namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in organization requirements.</summary>
public sealed class OrganizationBuilder
{
    private readonly ConfigBuilder _configuration;

    internal OrganizationBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets top-level type declarations in source files.</summary>
    public FileOrganizationRuleChain Files() => new(_configuration);
}
