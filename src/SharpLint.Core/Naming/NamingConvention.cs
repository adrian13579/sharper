namespace SharpLint.Core.Naming;

/// <summary>
/// Casing styles a naming rule can require or forbid.
/// </summary>
public enum NamingConvention
{
    /// <summary>Each word capitalized, no separators, e.g. <c>CustomerOrder</c>.</summary>
    PascalCase,

    /// <summary>First word lowercase, subsequent words capitalized, e.g. <c>customerOrder</c>.</summary>
    CamelCase,

    /// <summary>Lowercase words separated by underscores, e.g. <c>customer_order</c>.</summary>
    SnakeCase,

    /// <summary>Uppercase words separated by underscores, e.g. <c>CUSTOMER_ORDER</c>; typical for constants.</summary>
    UpperSnakeCase,
}
