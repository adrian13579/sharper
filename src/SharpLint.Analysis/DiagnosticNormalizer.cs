using System.Globalization;
using System.Text.Json;

namespace SharpLint.Analysis;

public static class DiagnosticNormalizer
{
    public static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    public static string? FromUri(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var value) && value.IsFile
            ? Path.GetFullPath(value.LocalPath) : null;

    public static IReadOnlyList<SharpLintDiagnostic> Parse(JsonElement items, string? uri)
    {
        var path = FromUri(uri);
        return items.EnumerateArray().Select(item =>
        {
            var (line, column, endLine, endColumn) = Position(item);
            return new SharpLintDiagnostic
            {
                Code = item.TryGetProperty("code", out var code) ? code.ToString() : "",
                Message = item.GetProperty("message").GetString() ?? "",
                Severity = item.TryGetProperty("severity", out var severity) ? severity.GetInt32() switch
                {
                    1 => "error", 2 => "warning", 3 => "information", 4 => "hint",
                    var unknown => unknown.ToString(CultureInfo.InvariantCulture),
                } : "unspecified",
                FilePath = path, StartLine = line, StartColumn = column, EndLine = endLine, EndColumn = endColumn,
                Source = item.TryGetProperty("source", out var source) ? source.GetString() : null,
                HelpUrl = item.TryGetProperty("codeDescription", out var description) ? description.GetProperty("href").GetString() : null,
                Data = item.TryGetProperty("data", out var data) ? data.Clone() : null,
                Tags = item.TryGetProperty("tags", out var tags) ? tags.EnumerateArray().Select(t => t.GetInt32()).ToArray() : null,
                RelatedInformation = item.TryGetProperty("relatedInformation", out var related)
                    ? related.EnumerateArray().Select(info =>
                    {
                        var location = info.GetProperty("location");
                        var (l, c, el, ec) = Position(location);
                        return new RelatedDiagnostic(info.GetProperty("message").GetString() ?? "",
                            FromUri(location.GetProperty("uri").GetString()), l, c, el, ec);
                    }).ToArray() : null,
            };
        }).ToArray();
    }

    // Dedupe only byte-equivalent normalized findings, including related information, tags and opaque data.
    // Different projects/providers can supply distinct data; these must not be merged by code and position.
    public static IReadOnlyList<SharpLintDiagnostic> Order(IEnumerable<SharpLintDiagnostic> diagnostics)
        => diagnostics.Select(d => (Diagnostic: d, Key: JsonSerializer.Serialize(d, DiagnosticJson.Options)))
            .DistinctBy(d => d.Key, StringComparer.Ordinal)
            .OrderBy(d => d.Diagnostic.FilePath, StringComparer.Ordinal)
            .ThenBy(d => d.Diagnostic.StartLine).ThenBy(d => d.Diagnostic.StartColumn)
            .ThenBy(d => d.Diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Diagnostic).ToArray();

    private static (int?, int?, int?, int?) Position(JsonElement item)
        => item.TryGetProperty("range", out var range)
            ? (range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32(),
               range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32())
            : (null, null, null, null);
}
