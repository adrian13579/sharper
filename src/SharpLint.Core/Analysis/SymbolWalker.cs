using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpLint.Core.Naming;
using SymbolKind = SharpLint.Core.Naming.SymbolKind;

namespace SharpLint.Core.Analysis;

internal static class SymbolWalker
{
    public static IEnumerable<SymbolInfo> Walk(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case MethodDeclarationSyntax m:
                    yield return new SymbolInfo(
                        SymbolKind.Method,
                        AccessibilityFlags(m.Modifiers, DefaultAccessibilityFor(m))
                            | (m.Modifiers.Any(SyntaxKind.AsyncKeyword) ? PredicateFlags.Async : PredicateFlags.None),
                        m.Identifier.Text,
                        m,
                        m.Identifier.GetLocation());
                    break;

                case ClassDeclarationSyntax c:
                    yield return new SymbolInfo(
                        SymbolKind.Class,
                        PredicateFlags.None,
                        c.Identifier.Text,
                        c,
                        c.Identifier.GetLocation());
                    break;

                case InterfaceDeclarationSyntax i:
                    yield return new SymbolInfo(
                        SymbolKind.Interface,
                        PredicateFlags.None,
                        i.Identifier.Text,
                        i,
                        i.Identifier.GetLocation());
                    break;

                case RecordDeclarationSyntax r:
                    yield return new SymbolInfo(
                        SymbolKind.Record,
                        PredicateFlags.None,
                        r.Identifier.Text,
                        r,
                        r.Identifier.GetLocation());
                    break;

                case EnumDeclarationSyntax e:
                    yield return new SymbolInfo(
                        SymbolKind.Enum,
                        PredicateFlags.None,
                        e.Identifier.Text,
                        e,
                        e.Identifier.GetLocation());
                    break;

                case PropertyDeclarationSyntax p:
                    yield return new SymbolInfo(
                        SymbolKind.Property,
                        PredicateFlags.None,
                        p.Identifier.Text,
                        p,
                        p.Identifier.GetLocation());
                    break;

                case FieldDeclarationSyntax f:
                    {
                        var flags = AccessibilityFlags(f.Modifiers, PredicateFlags.Private);
                        if (f.Modifiers.Any(SyntaxKind.ConstKeyword)) flags |= PredicateFlags.Const;
                        foreach (var v in f.Declaration.Variables)
                        {
                            yield return new SymbolInfo(
                                SymbolKind.Field,
                                flags,
                                v.Identifier.Text,
                                f,
                                v.Identifier.GetLocation());
                        }
                        break;
                    }

                case ParameterSyntax pr when pr.Identifier.Text.Length > 0:
                    yield return new SymbolInfo(
                        SymbolKind.Parameter,
                        PredicateFlags.None,
                        pr.Identifier.Text,
                        pr,
                        pr.Identifier.GetLocation());
                    break;

                case LocalDeclarationStatementSyntax local:
                    foreach (var v in local.Declaration.Variables)
                    {
                        yield return new SymbolInfo(
                            SymbolKind.LocalVariable,
                            PredicateFlags.None,
                            v.Identifier.Text,
                            local,
                            v.Identifier.GetLocation());
                    }
                    break;

                case NamespaceDeclarationSyntax ns:
                    yield return new SymbolInfo(
                        SymbolKind.Namespace,
                        PredicateFlags.None,
                        ns.Name.ToString(),
                        ns,
                        ns.Name.GetLocation());
                    break;

                case FileScopedNamespaceDeclarationSyntax fns:
                    yield return new SymbolInfo(
                        SymbolKind.Namespace,
                        PredicateFlags.None,
                        fns.Name.ToString(),
                        fns,
                        fns.Name.GetLocation());
                    break;
            }
        }
    }

    private static PredicateFlags DefaultAccessibilityFor(MethodDeclarationSyntax method) =>
        method.Parent is InterfaceDeclarationSyntax ? PredicateFlags.Public : PredicateFlags.Private;

    private static PredicateFlags AccessibilityFlags(SyntaxTokenList modifiers, PredicateFlags defaultAccess)
    {
        var hasPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        var hasInternal = modifiers.Any(SyntaxKind.InternalKeyword);
        var hasProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        var hasPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);

        if (!hasPublic && !hasInternal && !hasProtected && !hasPrivate)
        {
            return defaultAccess;
        }

        var flags = PredicateFlags.None;
        if (hasPublic) flags |= PredicateFlags.Public;
        if (hasInternal) flags |= PredicateFlags.Internal;
        if (hasProtected) flags |= PredicateFlags.Protected;
        if (hasPrivate) flags |= PredicateFlags.Private;
        return flags;
    }
}
