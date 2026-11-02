namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for catch blocks; modifiers affect the most recent requirement.</summary>
public sealed class CatchBlockRuleChain : BuiltInRuleChain<CatchBlockRuleChain>
{
    internal CatchBlockRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids catch blocks containing no statements.</summary>
    public CatchBlockRuleChain ForbidEmpty() => Add("reliability.empty-catch");
}
