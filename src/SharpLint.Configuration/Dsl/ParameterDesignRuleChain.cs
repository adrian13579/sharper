namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for declared parameter lists; modifiers affect the most recent requirement.</summary>
public sealed class ParameterDesignRuleChain : BuiltInRuleChain<ParameterDesignRuleChain>
{
    internal ParameterDesignRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Limits declared parameter lists to the given positive count.</summary>
    public ParameterDesignRuleChain RequireMaximumCount(int maximum = 5)
    {
        ValidateMaximum(maximum);
        return Add("design.maximum-parameters", new { maximum });
    }
}
