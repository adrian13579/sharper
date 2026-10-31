namespace SharpLint.Core.Rules;

/// <summary>The core category catalog. Accessing a category does not enable any rules.</summary>
public static class Rules
{
    public const string CoreModuleId = "sharplint";

    public static RuleCategory Naming { get; } = new("naming", "Naming");
    public static RuleCategory Syntax { get; } = new("syntax", "Syntax");
    public static RuleCategory Organization { get; } = new("organization", "Organization");
    public static RuleCategory Design { get; } = new("design", "Design");
    public static RuleCategory Dependencies { get; } = new("dependencies", "Dependencies");
    public static RuleCategory Architecture { get; } = new("architecture", "Architecture");
    public static RuleCategory Reliability { get; } = new("reliability", "Reliability");
    public static RuleCategory Performance { get; } = new("performance", "Performance");
    public static RuleCategory Project { get; } = new("project", "Project");

    public static IReadOnlyList<RuleCategory> All { get; } = Array.AsReadOnly<RuleCategory>(
        [Naming, Syntax, Organization, Design, Dependencies, Architecture, Reliability, Performance, Project]);
}
