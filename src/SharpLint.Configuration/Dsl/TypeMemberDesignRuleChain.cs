namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for direct type member declarations; modifiers affect the most recent requirement.</summary>
public sealed class TypeMemberDesignRuleChain : BuiltInRuleChain<TypeMemberDesignRuleChain>
{
    internal TypeMemberDesignRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Limits direct type member declarations to the given positive count.</summary>
    public TypeMemberDesignRuleChain RequireMaximumCount(int maximum = 20)
    {
        ValidateMaximum(maximum);
        return Add("design.maximum-type-members", new { maximum });
    }
}
