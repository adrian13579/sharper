using System.Text.Json;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;

namespace SharpLint.Configuration.Dsl;

/// <summary>Configures policies owned by one built-in or external module.</summary>
public sealed class ModuleBuilder
{
    private readonly ConfigBuilder _configuration;
    private readonly RuleModule _module;

    internal ModuleBuilder(ConfigBuilder configuration, RuleModule module)
    {
        _configuration = configuration;
        _module = module;
    }

    /// <summary>Enables a module policy by its full stable ID, with JSON-serializable options.</summary>
    public ModuleBuilder Rule(string ruleId, object? options = null, Severity severity = Severity.Warning)
    {
        if (!_module.Rules.Any(r => r.Definition.Id == ruleId))
            throw new ArgumentException($"Module '{_module.Id}' does not provide rule '{ruleId}'.", nameof(ruleId));
        _configuration.Rule(ruleId, JsonSerializer.SerializeToElement(options ?? new { }), severity);
        return this;
    }
}
