using System.Globalization;
using Microsoft.CodeAnalysis;
using SharpLint.Core.Diagnostics;
using Diagnostic = SharpLint.Core.Diagnostics.Diagnostic;

namespace SharpLint.Analysis;

public static class CompilerDiagnosticAdapter
{
    public static string ProjectIdentity(Project project) =>
        (project.FilePath ?? project.Name) + "::" + project.Name;

    public static Diagnostic Convert(Microsoft.CodeAnalysis.Diagnostic diagnostic, string project)
    {
        var source = diagnostic.Location.Kind is LocationKind.SourceFile or LocationKind.ExternalFile;
        var span = source ? diagnostic.Location.GetLineSpan() : default;
        return new Diagnostic(diagnostic.Id, diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => Severity.Error,
            DiagnosticSeverity.Warning => Severity.Warning,
            DiagnosticSeverity.Info => Severity.Info,
            _ => Severity.Hidden,
        }, diagnostic.GetMessage(CultureInfo.InvariantCulture), source
            ? new SourceLocation(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1)
            : default)
        {
            Origin = DiagnosticOrigin.Compiler,
            CategoryId = diagnostic.Descriptor.Category,
            Project = project,
            HelpUrl = string.IsNullOrEmpty(diagnostic.Descriptor.HelpLinkUri) ? null : diagnostic.Descriptor.HelpLinkUri,
            HasSourceLocation = source,
            SpanStart = diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Start : null,
            SpanLength = diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Length : null,
        };
    }
}
