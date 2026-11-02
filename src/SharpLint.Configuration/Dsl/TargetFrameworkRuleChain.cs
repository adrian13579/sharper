namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for literal target framework declarations in projects; modifiers affect the most recent requirement.</summary>
public sealed class TargetFrameworkRuleChain : BuiltInRuleChain<TargetFrameworkRuleChain>
{
    internal TargetFrameworkRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Restricts explicit target framework declarations to this allowlist.</summary>
    public TargetFrameworkRuleChain RequireAllowed(params string[] allowed)
    {
        ValidateValues(allowed);
        return Add("project.target-frameworks", new { allowed });
    }
}
