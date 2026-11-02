namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for namespace declaration syntax; modifiers affect the most recent requirement.</summary>
public sealed class NamespaceSyntaxRuleChain : BuiltInRuleChain<NamespaceSyntaxRuleChain>
{
    internal NamespaceSyntaxRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Requires namespace declarations to use file-scoped syntax.</summary>
    public NamespaceSyntaxRuleChain RequireFileScoped() => Add("syntax.file-scoped-namespaces");
}
