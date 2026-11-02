namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in design requirements.</summary>
public sealed class DesignBuilder
{
    private readonly ConfigBuilder _configuration;

    internal DesignBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets declared parameter lists.</summary>
    public ParameterDesignRuleChain Parameters() => new(_configuration);

    /// <summary>Targets direct type member declarations.</summary>
    public TypeMemberDesignRuleChain TypeMembers() => new(_configuration);
}
