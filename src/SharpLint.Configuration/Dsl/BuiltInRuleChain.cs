using System.Text.Json;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Configuration.Dsl;

/// <summary>A built-in policy chain whose severity modifiers affect the most recent requirement.</summary>
/// <typeparam name="TChain">The concrete selector chain, preserved by fluent modifiers.</typeparam>
public abstract class BuiltInRuleChain<TChain> where TChain : BuiltInRuleChain<TChain>
{
    private readonly ConfigBuilder _configuration;
    private string? _lastRuleId;

    private protected BuiltInRuleChain(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Reports the most recent requirement as an error.</summary>
    public TChain AsError() => SetSeverity(Severity.Error);

    /// <summary>Reports the most recent requirement as a warning, the default severity.</summary>
    public TChain AsWarning() => SetSeverity(Severity.Warning);

    /// <summary>Reports the most recent requirement as information.</summary>
    public TChain AsInfo() => SetSeverity(Severity.Info);

    /// <summary>Disables execution of the most recent requirement.</summary>
    public TChain Disabled() => SetSeverity(Severity.Hidden);

    private protected TChain Add(string id, object? options = null)
    {
        _configuration.Rule(id, JsonSerializer.SerializeToElement(options ?? new { }));
        _lastRuleId = id;
        return (TChain)this;
    }

    private protected static void ValidateMaximum(int maximum) => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

    private protected static void ValidateValues(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Supply at least one nonblank value.", nameof(values));
    }

    private TChain SetSeverity(Severity severity)
    {
        if (_lastRuleId is null)
            throw new InvalidOperationException("A modifier was called before any Require…/Forbid… verb on this selector.");
        _configuration.SetRuleSeverity(_lastRuleId, severity);
        return (TChain)this;
    }
}
