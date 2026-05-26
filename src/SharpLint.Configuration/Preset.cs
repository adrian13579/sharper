using SharpLint.Core.Naming;

namespace SharpLint.Configuration;

/// <summary>
/// Ready-made rule bundles to pass to <see cref="ConfigBuilder.UsePreset"/>.
/// </summary>
public static class Preset
{
    /// <summary>
    /// SharpLint's recommended baseline: PascalCase types, properties, and public methods;
    /// <c>I</c>-prefixed interfaces; <c>_camelCase</c> private fields; <c>UPPER_SNAKE_CASE</c> constants;
    /// <c>Async</c>-suffixed async methods; and camelCase parameters and locals.
    /// </summary>
    public static readonly Action<ConfigBuilder> Recommended = config =>
    {
        config.Naming(n =>
        {
            n.Classes().RequireConvention(NamingConvention.PascalCase);

            n.Interfaces()
                .RequirePrefix("I")
                .RequireConvention(NamingConvention.PascalCase);

            n.Methods(m => m.Public()).RequireConvention(NamingConvention.PascalCase);
            n.Methods(m => m.Async()).RequireSuffix("Async");

            n.Properties().RequireConvention(NamingConvention.PascalCase);

            n.Fields(f => f.Private())
                .RequirePrefix("_")
                .RequireConvention(NamingConvention.CamelCase);
            n.Fields(f => f.Const()).RequireConvention(NamingConvention.UpperSnakeCase);

            n.Parameters().RequireConvention(NamingConvention.CamelCase);
            n.LocalVariables().RequireConvention(NamingConvention.CamelCase);
        });
    };
}
