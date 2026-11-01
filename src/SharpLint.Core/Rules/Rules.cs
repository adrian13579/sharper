namespace SharpLint.Core.Rules;

/// <summary>The built-in module catalog. Rules execute only when configured.</summary>
public static class Rules
{
    public const string CoreModuleId = "sharplint";

    public static RuleModule Naming { get; } = BuiltInPolicies.CreateModule("naming", "Naming");
    public static RuleModule Syntax { get; } = BuiltInPolicies.CreateModule("syntax", "Syntax");
    public static RuleModule Organization { get; } = BuiltInPolicies.CreateModule("organization", "Organization");
    public static RuleModule Design { get; } = BuiltInPolicies.CreateModule("design", "Design");
    public static RuleModule Dependencies { get; } = BuiltInPolicies.CreateModule("dependencies", "Dependencies");
    public static RuleModule Architecture { get; } = BuiltInPolicies.CreateModule("architecture", "Architecture");
    public static RuleModule Reliability { get; } = BuiltInPolicies.CreateModule("reliability", "Reliability");
    public static RuleModule Performance { get; } = BuiltInPolicies.CreateModule("performance", "Performance");
    public static RuleModule Project { get; } = BuiltInPolicies.CreateModule("project", "Project");

    public static IReadOnlyList<RuleModule> All { get; } = Array.AsReadOnly<RuleModule>(
        [Naming, Syntax, Organization, Design, Dependencies, Architecture, Reliability, Performance, Project]);
}
