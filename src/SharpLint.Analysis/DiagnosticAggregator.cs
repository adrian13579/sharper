using SharpLint.Core.Diagnostics;

namespace SharpLint.Analysis;

public static class DiagnosticAggregator
{
    public static Diagnostic[] Aggregate(IEnumerable<Diagnostic> diagnostics) => diagnostics.Distinct()
        .OrderBy(d => d.Location.FilePath?.Replace('\\', '/'), StringComparer.Ordinal)
        .ThenBy(d => d.Location.Line).ThenBy(d => d.Location.Column)
        .ThenBy(d => d.Project, StringComparer.Ordinal).ThenBy(d => d.Origin)
        .ThenBy(d => d.RuleId, StringComparer.Ordinal).ThenBy(d => d.Severity)
        .ThenBy(d => d.Message, StringComparer.Ordinal).ThenBy(d => d.SpanStart).ThenBy(d => d.SpanLength)
        .ThenBy(d => d.CategoryId, StringComparer.Ordinal).ThenBy(d => d.HelpUrl, StringComparer.Ordinal)
        .ThenBy(d => d.Suggestion, StringComparer.Ordinal).ThenBy(d => d.HasSourceLocation)
        .ThenBy(d => d.Location.FilePath, StringComparer.Ordinal).ToArray();
}
