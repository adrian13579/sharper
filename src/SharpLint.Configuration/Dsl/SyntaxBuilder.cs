namespace SharpLint.Configuration.Dsl;

/// <summary>Selects targets for built-in syntax requirements.</summary>
public sealed class SyntaxBuilder
{
    private readonly ConfigBuilder _configuration;

    internal SyntaxBuilder(ConfigBuilder configuration) => _configuration = configuration;

    /// <summary>Targets goto statements.</summary>
    public GotoStatementRuleChain GotoStatements() => new(_configuration);

    /// <summary>Targets namespace declaration syntax.</summary>
    public NamespaceSyntaxRuleChain Namespaces() => new(_configuration);
}
