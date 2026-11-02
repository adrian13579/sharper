namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in architecture requirements.</summary>
public sealed class ArchitectureBuilder
{
    private readonly ConfigBuilder _configuration;

    internal ArchitectureBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets references from a namespace and its descendants.</summary>
    public NamespaceBoundaryRuleChain Namespaces(string from)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        return new(_configuration, from);
    }

    /// <summary>Targets explicit base class and interface declarations.</summary>
    public InheritanceRuleChain Inheritance() => new(_configuration);
}
