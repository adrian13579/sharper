namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for literal nullable declarations in projects; modifiers affect the most recent requirement.</summary>
public sealed class NullableProjectRuleChain : BuiltInRuleChain<NullableProjectRuleChain>
{
    internal NullableProjectRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Requires explicit enabled nullable declarations in each project.</summary>
    public NullableProjectRuleChain RequireEnabled() => Add("project.nullable-enabled");
}
