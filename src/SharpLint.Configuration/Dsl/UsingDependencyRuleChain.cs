namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for explicit using directives; modifiers affect the most recent requirement.</summary>
public sealed class UsingDependencyRuleChain : BuiltInRuleChain<UsingDependencyRuleChain>
{
    internal UsingDependencyRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids using names matching these namespaces or their dotted descendants.</summary>
    public UsingDependencyRuleChain ForbidNamespaces(params string[] namespaces)
    {
        ValidateValues(namespaces);
        return Add("dependencies.forbidden-usings", new { namespaces });
    }
}
