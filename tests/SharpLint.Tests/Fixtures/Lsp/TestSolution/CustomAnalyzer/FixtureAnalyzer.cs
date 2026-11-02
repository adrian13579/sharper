using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FixtureAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FixtureAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new("FIXTURE001", "Custom fixture rule", "Custom analyzer observed '{0}'", "Testing", DiagnosticSeverity.Warning, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c =>
        {
            var declaration = (ClassDeclarationSyntax)c.Node;
            if (declaration.Identifier.ValueText == "CustomTarget")
                c.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), declaration.Identifier.ValueText));
        }, SyntaxKind.ClassDeclaration);
    }
}
