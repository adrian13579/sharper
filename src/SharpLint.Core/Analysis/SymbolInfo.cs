using Microsoft.CodeAnalysis;
using SharpLint.Core.Naming;
using SymbolKind = SharpLint.Core.Naming.SymbolKind;

namespace SharpLint.Core.Analysis;

internal sealed record SymbolInfo(
    SymbolKind Kind,
    PredicateFlags Predicates,
    string Name,
    SyntaxNode DeclaringNode,
    Location Location);
