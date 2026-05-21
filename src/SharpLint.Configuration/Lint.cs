namespace SharpLint.Configuration;

public static class Lint
{
    public static SharpLintConfig Configure(Action<ConfigBuilder> configure)
    {
        var builder = new ConfigBuilder();
        configure(builder);
        return builder.Build();
    }
}
