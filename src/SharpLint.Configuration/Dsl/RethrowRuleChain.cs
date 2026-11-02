namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for rethrows of the enclosing caught exception; modifiers affect the most recent requirement.</summary>
public sealed class RethrowRuleChain : BuiltInRuleChain<RethrowRuleChain>
{
    internal RethrowRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Requires a bare rethrow instead of throwing the caught variable by name.</summary>
    public RethrowRuleChain RequirePreservedStackTrace() => Add("reliability.rethrow-caught-exception");
}
