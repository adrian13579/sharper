namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for literal package declarations; modifiers affect the most recent requirement.</summary>
public sealed class PackageDependencyRuleChain : BuiltInRuleChain<PackageDependencyRuleChain>
{
    internal PackageDependencyRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids complete package IDs, ignoring case.</summary>
    public PackageDependencyRuleChain ForbidNames(params string[] packages)
    {
        ValidateValues(packages);
        return Add("dependencies.forbidden-packages", new { packages });
    }
}
