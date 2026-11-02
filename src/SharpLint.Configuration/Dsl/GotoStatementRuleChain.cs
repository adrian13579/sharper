namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for goto statements; modifiers affect the most recent requirement.</summary>
public sealed class GotoStatementRuleChain : BuiltInRuleChain<GotoStatementRuleChain>
{
    internal GotoStatementRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids goto statements, including goto case and goto default.</summary>
    public GotoStatementRuleChain Forbid() => Add("syntax.no-goto");
}
