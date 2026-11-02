namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for explicit cast expressions; modifiers affect the most recent requirement.</summary>
public sealed class ConversionRuleChain : BuiltInRuleChain<ConversionRuleChain>
{
    internal ConversionRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids explicit casts classified as boxing by Roslyn.</summary>
    public ConversionRuleChain ForbidExplicitBoxing() => Add("performance.explicit-boxing");
}
