namespace SharpLint.Core.Rules;

/// <summary>An engineering concern, independent of rule scope, provider, and implementation.</summary>
public sealed record RuleCategory
{
    public RuleCategory(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }
    public string DisplayName { get; }
}
