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

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source, path: file, cancellationToken: cancellationToken);
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            foreach (var symbol in SymbolWalker.Walk(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var rule in ResolveRules(context.NamingRules, symbol))
                {
                    if (rule.Severity == Severity.Hidden) continue;
                    if (NamingRuleEvaluator.IsExempt(symbol, rule)) continue;
                    if (!NamingRuleEvaluator.IsViolated(rule, symbol.Name)) continue;

                    diagnostics.Add(NamingRuleEvaluator.CreateDiagnostic(rule, symbol));
                }
            }
        }

        stopwatch.Stop();
        return new AnalysisResult(diagnostics, stopwatch.Elapsed, files.Count);
    }

    // For each verb, the last applicable declaration selects the winning chain. Every constraint
    // of that verb in the winning chain composes with AND. Other verbs resolve independently.
    // Hidden rules participate in precedence, then are skipped individually during evaluation.
    // Rules without chain metadata retain the standalone, last-declaration-wins behavior.
    private static IEnumerable<NamingRule> ResolveRules(IReadOnlyList<NamingRule> rules, SymbolInfo symbol) =>
        rules
            .Where(r => NamingRuleEvaluator.Applies(r, symbol))
            .GroupBy(r => r.Verb)
            .SelectMany(g =>
            {
                var last = g.Last();
                return last.ChainId is null
                    ? new[] { last }
                    : g.Where(r => r.ChainId == last.ChainId);
            });
}
