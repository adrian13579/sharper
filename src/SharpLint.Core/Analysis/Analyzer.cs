using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Core.Analysis;

public sealed class Analyzer
{
    public async Task<AnalysisResult> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<Diagnostic>();

        var files = Directory.EnumerateFiles(context.WorkingDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var activeRules = context.NamingRules
            .Where(r => r.Severity != Severity.Hidden)
            .ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source, path: file, cancellationToken: cancellationToken);
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            foreach (var symbol in SymbolWalker.Walk(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var rule in activeRules)
                {
                    if (!NamingRuleEvaluator.Applies(rule, symbol)) continue;
                    if (NamingRuleEvaluator.IsExempt(symbol, rule)) continue;
                    if (!NamingRuleEvaluator.IsViolated(rule, symbol.Name)) continue;

                    diagnostics.Add(NamingRuleEvaluator.CreateDiagnostic(rule, symbol));
                }
            }
        }

        stopwatch.Stop();
        return new AnalysisResult(diagnostics, stopwatch.Elapsed, files.Count);
    }
}
