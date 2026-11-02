namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for references from a namespace and its descendants; modifiers affect the most recent requirement.</summary>
public sealed class NamespaceBoundaryRuleChain : BuiltInRuleChain<NamespaceBoundaryRuleChain>
{
    private readonly string _from;

    internal NamespaceBoundaryRuleChain(ConfigBuilder configuration, string from) : base(configuration) => _from = from;

    /// <summary>Forbids resolved references to these namespaces and their descendants.</summary>
    public NamespaceBoundaryRuleChain ForbidReferencesTo(params string[] namespaces)
    {
        ValidateValues(namespaces);
        return Add("architecture.namespace-boundaries", new { from = _from, to = namespaces });
    }
}
