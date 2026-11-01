using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpLint.Core.Rules;

/// <summary>The initial built-in policies. Naming keeps its specialized selector-chain evaluator.</summary>
internal sealed class BuiltInPolicies(string id) : IRuleAnalyzer
{
    private static readonly (string Id, RuleScope Scope)[] Definitions =
    [
        ("syntax.no-goto", RuleScope.SourceFile),
        ("syntax.file-scoped-namespaces", RuleScope.Namespace),
        ("organization.one-type-per-file", RuleScope.SourceFile),
        ("organization.match-type-file-name", RuleScope.Type),
        ("design.maximum-parameters", RuleScope.Symbol),
        ("design.maximum-type-members", RuleScope.Type),
        ("dependencies.forbidden-usings", RuleScope.SourceFile),
        ("dependencies.forbidden-packages", RuleScope.Project),
        ("architecture.namespace-boundaries", RuleScope.Repository),
        ("architecture.forbidden-inheritance", RuleScope.Type),
        ("reliability.empty-catch", RuleScope.Symbol),
        ("reliability.rethrow-caught-exception", RuleScope.Symbol),
        ("performance.empty-array-allocation", RuleScope.Symbol),
        ("performance.explicit-boxing", RuleScope.Symbol),
        ("project.nullable-enabled", RuleScope.Project),
        ("project.target-frameworks", RuleScope.Project),
    ];

    internal static RuleModule CreateModule(string id, string displayName) => new(id, [],
        Definitions.Where(d => d.Id.StartsWith(id + ".", StringComparison.Ordinal))
            .Select(d => new RuleRegistration(new(d.Id, id, d.Scope)
            {
                InputRequirement = d.Id.StartsWith("architecture.", StringComparison.Ordinal)
                    || d.Id == "performance.explicit-boxing"
                    ? RuleInputRequirement.ErrorFreeCompilation : RuleInputRequirement.Syntax,
            }, new BuiltInPolicies(d.Id))).ToArray()) { DisplayName = displayName };

    public async ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(
        RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken)
    {
        if (options.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Options for '{id}' must be a JSON object.");
        if (id is "dependencies.forbidden-packages" or "project.nullable-enabled" or "project.target-frameworks")
            return await ProjectPolicies.AnalyzeAsync(id, context, options, cancellationToken).ConfigureAwait(false);

        var maximum = id switch
        {
            "design.maximum-parameters" => PositiveInteger(options, "maximum", 5),
            "design.maximum-type-members" => PositiveInteger(options, "maximum", 20),
            _ => 0,
        };
        var namespaces = id == "dependencies.forbidden-usings" ? Strings(options, "namespaces") : [];
        var from = id == "architecture.namespace-boundaries" ? Text(options, "from") : "";
        var to = id == "architecture.namespace-boundaries" ? Strings(options, "to") : [];
        var types = id == "architecture.forbidden-inheritance" ? Strings(options, "types") : [];
        var result = new List<RuleViolation>();
        foreach (var tree in context.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var topLevelTypes = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
                .Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                    && n.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax).ToArray();
            SemanticModel? model = null;
            if (id.StartsWith("architecture.", StringComparison.Ordinal) || id == "performance.explicit-boxing")
            {
                var compilation = context.Compilations.FirstOrDefault(c => c.ContainsSyntaxTree(tree))
                    ?? throw new InvalidOperationException($"Rule '{id}' requires a project compilation containing '{tree.FilePath}'.");
                model = compilation.GetSemanticModel(tree);
            }
            foreach (var node in root.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (id)
                {
                    case "syntax.no-goto" when node is GotoStatementSyntax:
                        Add(node, "Avoid goto statements.");
                        break;
                    case "syntax.file-scoped-namespaces" when node is NamespaceDeclarationSyntax:
                        Add(node, "Use a file-scoped namespace.");
                        break;
                    case "organization.one-type-per-file" when topLevelTypes.Length > 1 && topLevelTypes.Skip(1).Contains(node):
                        Add(node, "Declare at most one top-level type per file.");
                        break;
                    case "organization.match-type-file-name" when topLevelTypes.Contains(node):
                        var name = node is BaseTypeDeclarationSyntax type ? type.Identifier.ValueText
                            : ((DelegateDeclarationSyntax)node).Identifier.ValueText;
                        if (!string.Equals(Path.GetFileNameWithoutExtension(tree.FilePath), name, StringComparison.Ordinal))
                            Add(node, $"Type '{name}' must be declared in '{name}.cs'.");
                        break;
                    case "design.maximum-parameters" when node is BaseParameterListSyntax parameters && parameters.Parameters.Count > maximum:
                        Add(node, $"Declaration has {parameters.Parameters.Count} parameters; maximum is {maximum}.");
                        break;
                    case "design.maximum-type-members" when node is TypeDeclarationSyntax declaration && declaration.Members.Count > maximum:
                        Add(node, $"Type has {declaration.Members.Count} members; maximum is {maximum}.");
                        break;
                    case "dependencies.forbidden-usings" when node is UsingDirectiveSyntax { Name: { } used }:
                        if (namespaces.Any(n => InNamespace(used.ToString().Replace("global::", "", StringComparison.Ordinal), n)))
                            Add(node, $"Using '{used}' is forbidden.");
                        break;
                    case "architecture.namespace-boundaries" when node is NameSyntax reference:
                        // Only report the outermost name. Semantic resolution catches aliases and fully qualified references.
                        if (reference.Parent is NameSyntax or BaseNamespaceDeclarationSyntax) break;
                        var enclosingSymbol = model!.GetEnclosingSymbol(reference.SpanStart, cancellationToken);
                        var owner = enclosingSymbol is INamespaceSymbol ownerNamespace ? ownerNamespace.ToDisplayString()
                            : enclosingSymbol?.ContainingNamespace?.ToDisplayString();
                        var target = model.GetSymbolInfo(reference, cancellationToken).Symbol;
                        var targetNamespace = target is INamespaceSymbol ns ? ns.ToDisplayString() : target?.ContainingNamespace?.ToDisplayString();
                        if (owner is not null && targetNamespace is not null && InNamespace(owner, from)
                            && to.Any(n => InNamespace(targetNamespace, n)))
                            Add(reference, $"Namespace '{owner}' must not reference '{targetNamespace}'.");
                        break;
                    case "architecture.forbidden-inheritance" when node is BaseTypeSyntax baseType:
                        var inherited = model!.GetTypeInfo(baseType.Type, cancellationToken).Type?.ToDisplayString();
                        if (inherited is not null && types.Contains(inherited, StringComparer.Ordinal))
                            Add(baseType, $"Inheritance from '{inherited}' is forbidden.");
                        break;
                    case "reliability.empty-catch" when node is CatchClauseSyntax caught && caught.Block.Statements.Count == 0:
                        Add(caught, "Catch block contains no statements.");
                        break;
                    case "reliability.rethrow-caught-exception" when node is ThrowStatementSyntax { Expression: IdentifierNameSyntax thrown } statement:
                        var enclosing = statement.Ancestors().TakeWhile(n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                            .OfType<CatchClauseSyntax>().FirstOrDefault();
                        if (enclosing?.Declaration?.Identifier.ValueText == thrown.Identifier.ValueText)
                            Add(statement, "Use 'throw;' to preserve the caught exception's stack trace.");
                        break;
                    case "performance.empty-array-allocation" when node is ArrayCreationExpressionSyntax array:
                        if (array.Type.RankSpecifiers[0].Sizes.Count == 1
                            && array.Type.RankSpecifiers[0].Sizes[0] is LiteralExpressionSyntax size
                            && size.Token.Value is 0 or 0U or 0L or 0UL)
                            Add(array, "Empty array allocation; consider Array.Empty<T>() when shared identity is acceptable.");
                        break;
                    case "performance.explicit-boxing" when node is CastExpressionSyntax cast:
                        var sourceType = model!.GetTypeInfo(cast.Expression, cancellationToken).Type;
                        var targetType = model.GetTypeInfo(cast.Type, cancellationToken).Type;
                        if (sourceType is not null && targetType is not null
                            && ((CSharpCompilation)model!.Compilation).ClassifyConversion(sourceType, targetType).IsBoxing)
                            Add(cast, "Explicit conversion boxes a value type.");
                        break;
                }
            }
        }
        return result;

        void Add(SyntaxNode node, string message)
        {
            var span = node.GetLocation().GetLineSpan();
            var compilation = context.Compilations.FirstOrDefault(c => c.ContainsSyntaxTree(node.SyntaxTree));
            result.Add(new(message, new(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1))
            {
                Project = compilation is null ? null : context.CompilationProjects.GetValueOrDefault(compilation),
            });
        }
    }

    private static bool InNamespace(string value, string prefix) => value == prefix || value.StartsWith(prefix + ".", StringComparison.Ordinal);

    private static int PositiveInteger(JsonElement options, string key, int fallback)
    {
        if (!options.TryGetProperty(key, out var value)) return fallback;
        var number = 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out number) || number < 1)
            throw new ArgumentException($"'{key}' must be a positive integer.");
        return number;
    }

    internal static string Text(JsonElement options, string key)
    {
        if (!options.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"'{key}' must be a non-empty string.");
        return value.GetString()!;
    }

    internal static string[] Strings(JsonElement options, string key)
    {
        if (!options.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() == 0 || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString())))
            throw new ArgumentException($"'{key}' must be a non-empty array of non-empty strings.");
        return value.EnumerateArray().Select(v => v.GetString()!).ToArray();
    }
}
