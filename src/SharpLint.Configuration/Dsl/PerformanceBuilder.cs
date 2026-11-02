namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in performance requirements.</summary>
public sealed class PerformanceBuilder
{
    private readonly ConfigBuilder _configuration;

    internal PerformanceBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets explicit array allocations.</summary>
    public ArrayAllocationRuleChain Arrays() => new(_configuration);

    /// <summary>Targets explicit cast expressions.</summary>
    public ConversionRuleChain Conversions() => new(_configuration);
}
