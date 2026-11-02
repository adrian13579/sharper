namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for top-level type declarations in source files; modifiers affect the most recent requirement.</summary>
public sealed class FileOrganizationRuleChain : BuiltInRuleChain<FileOrganizationRuleChain>
{
    internal FileOrganizationRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Requires at most one top-level type per file.</summary>
    public FileOrganizationRuleChain RequireOneType() => Add("organization.one-type-per-file");

    /// <summary>Requires each top-level type name to match its file stem.</summary>
    public FileOrganizationRuleChain RequireMatchingTypeName() => Add("organization.match-type-file-name");
}
