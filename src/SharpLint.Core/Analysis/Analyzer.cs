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

    // Resolves rule precedence for a single symbol. Declaration order is precedence order (presets run
    // first, user rules append after), so among the rules that apply to the symbol the *last-declared*
    // one wins within each verb group. A later rule therefore overrides an earlier one — even a more
    // specific preset rule. Grouping by verb lets different constraint kinds coexist (e.g. a prefix rule
    // and a convention rule on the same field both apply); only same-verb rules compete. Hidden rules are
    // kept in the running so a later `.Disabled()` can win its group and shadow the rules it supersedes.
    private static IEnumerable<NamingRule> ResolveRules(IReadOnlyList<NamingRule> rules, SymbolInfo symbol) =>
        rules
            .Where(r => NamingRuleEvaluator.Applies(r, symbol))
            .GroupBy(r => r.Verb)
            .Select(g => g.Last());
}
