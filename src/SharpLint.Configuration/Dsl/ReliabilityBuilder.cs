namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in reliability requirements.</summary>
public sealed class ReliabilityBuilder
{
    private readonly ConfigBuilder _configuration;

    internal ReliabilityBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets catch blocks.</summary>
    public CatchBlockRuleChain CatchBlocks() => new(_configuration);

    /// <summary>Targets rethrows of the enclosing caught exception.</summary>
    public RethrowRuleChain Rethrows() => new(_configuration);
}
