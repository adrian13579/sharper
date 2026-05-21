using System.Text.RegularExpressions;

namespace SharpLint.Core.Naming;

public static class ConventionMatcher
{
    private static readonly Regex PascalCase = new("^_*[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex CamelCase = new("^_*[a-z][a-zA-Z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex UpperSnakeCase = new("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$", RegexOptions.Compiled);

    public static bool Matches(string identifier, NamingConvention convention) => convention switch
    {
        NamingConvention.PascalCase => PascalCase.IsMatch(identifier),
        NamingConvention.CamelCase => CamelCase.IsMatch(identifier),
        NamingConvention.SnakeCase => SnakeCase.IsMatch(identifier),
        NamingConvention.UpperSnakeCase => UpperSnakeCase.IsMatch(identifier),
        _ => throw new ArgumentOutOfRangeException(nameof(convention), convention, null),
    };

    public static bool MatchesNamespace(string fullNamespace, NamingConvention convention)
    {
        foreach (var segment in fullNamespace.Split('.'))
        {
            if (segment.Length == 0) return false;
            if (!Matches(segment, convention)) return false;
        }
        return true;
    }
}
