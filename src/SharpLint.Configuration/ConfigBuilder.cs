using System.Text.Json;
using SharpLint.Configuration.Dsl;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;

namespace SharpLint.Configuration;

/// <summary>
/// Accumulates the rules that make up a configuration. Supplied to the
/// <see cref="Lint.Configure"/> callback; chain its methods to declare rules.
/// </summary>
public sealed class ConfigBuilder
{
    private readonly List<NamingRule> _namingRules = [];
    private readonly List<ConfiguredRule> _rules = [];
    private readonly List<AnalyzerModuleReference> _modules = [];

    /// <summary>Registers and optionally configures an external module built from Roslyn analyzers.</summary>
    public ConfigBuilder UseModule(RuleModule module, Action<ModuleBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.Analyzers.Count == 0)
            throw new ArgumentException("External modules must provide Roslyn DiagnosticAnalyzer implementations.", nameof(module));
        if (Rules.All.Any(m => m.Id == module.Id) || module.Id == Rules.CoreModuleId || _modules.Any(m => m.Id == module.Id))
            throw new ArgumentException($"Duplicate or reserved module identifier '{module.Id}'.", nameof(module));
        var references = module.Analyzers.Select(analyzer =>
        {
            var type = analyzer.GetType();
            if (!type.IsVisible || type.GetConstructor(Type.EmptyTypes) is null || string.IsNullOrEmpty(type.Assembly.Location))
                throw new ArgumentException("Analyzer types must be public, parameterless, and stored in an assembly.", nameof(module));
            return new AnalyzerTypeReference(type.Assembly.Location, type.FullName!);
        }).ToArray();
        _modules.Add(new(module.Id, module.DisplayName, references));
        configure?.Invoke(new ModuleBuilder(this, module));
        return this;
    }

    /// <summary>Configures selected policies from a built-in module without enabling unrelated policies.</summary>
    public ConfigBuilder Module(RuleModule module, Action<ModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configure);
        if (!Rules.All.Contains(module))
            throw new ArgumentException("Register external Roslyn modules with UseModule.", nameof(module));
        configure(new ModuleBuilder(this, module));
        return this;
    }

    /// <summary>Configures a rule supplied by a module registered with the analysis host.</summary>
    public ConfigBuilder Rule(string ruleId, JsonElement options, Severity severity = Severity.Warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (_rules.Any(r => r.RuleId == ruleId))
            throw new ArgumentException($"Duplicate configured rule identifier '{ruleId}'.", nameof(ruleId));
        _rules.Add(new ConfiguredRule(ruleId, options.Clone(), severity));
        return this;
    }

    /// <summary>
    /// Applies a bundle of rules, such as <see cref="Preset.Recommended"/>, as a starting point.
    /// Rules declared afterwards add to (and can override) the preset.
    /// </summary>
    /// <param name="preset">A preset from <see cref="Preset"/>, or your own rule bundle.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ConfigBuilder UsePreset(Action<ConfigBuilder> preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset(this);
        return this;
    }

    /// <summary>
    /// Declares naming rules. Use the supplied <see cref="NamingBuilder"/> to pick a symbol kind
    /// (e.g. <c>n.Classes()</c>) and attach conventions, prefixes, suffixes, or patterns.
    /// </summary>
    /// <param name="configure">Callback that declares naming rules.</param>
    /// <returns>The same builder, for chaining.</returns>
    public ConfigBuilder Naming(Action<NamingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NamingBuilder(_namingRules));
        return this;
    }

    /// <summary>Declares preferred and forbidden C# constructs.</summary>
    public ConfigBuilder Syntax(Action<SyntaxBuilder> configure) => ConfigureCategory(configure, new SyntaxBuilder(this));

    /// <summary>Declares source file and type organization rules.</summary>
    public ConfigBuilder Organization(Action<OrganizationBuilder> configure) => ConfigureCategory(configure, new OrganizationBuilder(this));

    /// <summary>Declares parameter and type member limits.</summary>
    public ConfigBuilder Design(Action<DesignBuilder> configure) => ConfigureCategory(configure, new DesignBuilder(this));

    /// <summary>Declares explicit namespace import and package policies.</summary>
    public ConfigBuilder Dependencies(Action<DependenciesBuilder> configure) => ConfigureCategory(configure, new DependenciesBuilder(this));

    /// <summary>Declares semantic namespace and inheritance boundaries.</summary>
    public ConfigBuilder Architecture(Action<ArchitectureBuilder> configure) => ConfigureCategory(configure, new ArchitectureBuilder(this));

    /// <summary>Declares catch and rethrow policies.</summary>
    public ConfigBuilder Reliability(Action<ReliabilityBuilder> configure) => ConfigureCategory(configure, new ReliabilityBuilder(this));

    /// <summary>Declares allocation and conversion policies.</summary>
    public ConfigBuilder Performance(Action<PerformanceBuilder> configure) => ConfigureCategory(configure, new PerformanceBuilder(this));

    /// <summary>Declares literal project nullable and target framework policies.</summary>
    public ConfigBuilder Project(Action<ProjectBuilder> configure) => ConfigureCategory(configure, new ProjectBuilder(this));

    private ConfigBuilder ConfigureCategory<TBuilder>(Action<TBuilder> configure, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder);
        return this;
    }

    internal void SetRuleSeverity(string ruleId, Severity severity)
    {
        var index = _rules.FindIndex(rule => rule.RuleId == ruleId);
        _rules[index] = _rules[index] with { Severity = severity };
    }

    internal SharpLintConfig Build() => new(_namingRules.ToArray())
    {
        Rules = _rules.Count == 0 ? null : _rules.ToArray(),
        Modules = _modules.Count == 0 ? null : _modules.ToArray(),
    };
}
