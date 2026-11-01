namespace SharpLint.Core.Rules;

/// <summary>An immutable registration snapshot. Modules own rules; taxonomy is not an extension constraint.</summary>
public sealed class RuleRegistry
{
    private readonly Dictionary<string, (string ModuleId, RuleCategory Category)> _categories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string ModuleId, RuleRegistration Registration)> _rules = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<RuleModule> _modules;

    public static RuleRegistry Default { get; } = new([]);

    public RuleRegistry(IEnumerable<RuleModule> modules)
    {
        foreach (var module in Rules.All)
            _categories.Add(module.Id, (module.Id, new(module.Id, module.DisplayName)));
        var moduleList = Rules.All.Concat(modules).OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
        _modules = Array.AsReadOnly(moduleList);
        var moduleIds = new HashSet<string>(StringComparer.Ordinal) { Rules.CoreModuleId };
        foreach (var module in moduleList)
        {
            if (!moduleIds.Add(module.Id))
                throw new ArgumentException($"Duplicate module identifier '{module.Id}'.", nameof(modules));
            // Legacy category metadata remains available, but new Roslyn modules need no taxonomy registration.
            foreach (var category in module.Categories.OrderBy(c => c.Id, StringComparer.Ordinal))
                if (!_categories.TryAdd(category.Id, (module.Id, category)))
                    throw new ArgumentException($"Duplicate category identifier '{category.Id}'.", nameof(modules));
        }
        foreach (var module in moduleList)
        {
            foreach (var registration in module.Rules.OrderBy(r => r.Definition.Id, StringComparer.Ordinal))
            {
                var definition = registration.Definition;
                ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
                ArgumentNullException.ThrowIfNull(registration.Analyzer);
                if (module.Analyzers.Count == 0 && !_categories.ContainsKey(definition.CategoryId))
                    throw new ArgumentException($"Unknown category identifier '{definition.CategoryId}'.", nameof(modules));
                Add(module.Id, registration);
            }
        }
    }

    private void Add(string moduleId, RuleRegistration registration)
    {
        var id = registration.Definition.Id;
        if ((id.StartsWith("naming.", StringComparison.Ordinal) && moduleId != Rules.Naming.Id)
            || !_rules.TryAdd(id, (moduleId, registration)))
            throw new ArgumentException($"Duplicate or reserved rule identifier '{id}'.");
    }

    public IReadOnlyList<RuleModule> Modules => _modules;
    public IReadOnlyList<RuleCategory> Categories => _categories.Values.Select(c => c.Category)
        .OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
    public string GetCategoryModuleId(string categoryId) => _categories[categoryId].ModuleId;
    public string GetRuleModuleId(string ruleId) => _rules[ruleId].ModuleId;
    public RuleRegistration GetRule(string ruleId) => _rules.TryGetValue(ruleId, out var entry)
        ? entry.Registration
        : throw new InvalidOperationException($"Rule '{ruleId}' has no registered analyzer. Supply its module to the analysis host.");
}
