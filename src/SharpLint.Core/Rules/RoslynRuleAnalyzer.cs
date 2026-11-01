using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Core.Rules;

/// <summary>Adapts standard Roslyn analyzers without imposing the built-in module taxonomy.</summary>
internal sealed class RoslynRuleAnalyzer(DiagnosticAnalyzer analyzer, string ruleId) : IRuleAnalyzer
{
    public async ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(
        RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken)
    {
        var compilations = context.Compilations;
        if (compilations.Count == 0)
        {
            // Direct syntax hosts lack project references. This fallback supports syntax analyzers;
            // project hosts always supply their actual compilations for semantic analysis.
            compilations = [CSharpCompilation.Create("SharpLint.Syntax", context.SyntaxTrees,
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))];
        }
        var results = new List<RuleViolation>();
        foreach (var compilation in compilations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var specific = compilation.Options.SpecificDiagnosticOptions;
            foreach (var descriptor in analyzer.SupportedDiagnostics)
                specific = specific.SetItem(descriptor.Id, descriptor.Id == ruleId ? ReportDiagnostic.Warn : ReportDiagnostic.Suppress);
            // Explicit configuration enables even descriptors disabled by default. Roslyn still honors source suppressions.
            var configured = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(specific));
            Exception? failure = null;
            var driverOptions = new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([], new OptionsProvider(ruleId, options)),
                (exception, _, _) => Interlocked.CompareExchange(ref failure, exception, null),
                concurrentAnalysis: false, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false);
            var diagnostics = await configured.WithAnalyzers([analyzer], driverOptions)
                .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (failure is not null)
                throw new InvalidOperationException($"Roslyn analyzer for rule '{ruleId}' failed.", failure);
            foreach (var diagnostic in diagnostics.Where(d => d.Id == ruleId && !d.IsSuppressed))
            {
                var span = diagnostic.Location.GetLineSpan();
                results.Add(new(diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    diagnostic.Location.IsInSource
                        ? new(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1)
                        : new(context.WorkingDirectory, 1, 1))
                {
                    HasSourceLocation = diagnostic.Location.IsInSource,
                    Project = context.CompilationProjects.GetValueOrDefault(compilation),
                    HelpUrl = diagnostic.Descriptor.HelpLinkUri is { Length: > 0 } url ? url : null,
                    SpanStart = diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Start : null,
                    SpanLength = diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Length : null,
                });
            }
        }
        return results;
    }

    private sealed class OptionsProvider(string id, JsonElement options) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new JsonOptions(id, options);
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class JsonOptions(string id, JsonElement options) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            var prefix = $"sharplint.{id}.";
            if (key.StartsWith(prefix, StringComparison.Ordinal) && options.ValueKind == JsonValueKind.Object
                && options.TryGetProperty(key[prefix.Length..], out var property))
            {
                value = property.ValueKind == JsonValueKind.String ? property.GetString()! : property.GetRawText();
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
