namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for explicit base class and interface declarations; modifiers affect the most recent requirement.</summary>
public sealed class InheritanceRuleChain : BuiltInRuleChain<InheritanceRuleChain>
{
    internal InheritanceRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids explicit inheritance from these fully qualified semantic type names.</summary>
    public InheritanceRuleChain ForbidTypes(params string[] types)
    {
        ValidateValues(types);
        return Add("architecture.forbidden-inheritance", new { types });
    }
}
