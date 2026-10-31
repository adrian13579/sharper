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
                        AccessibilityFlags(m.Modifiers, DefaultMemberAccessibility(m))
                            | (m.Modifiers.Any(SyntaxKind.AsyncKeyword) ? PredicateFlags.Async : PredicateFlags.None),
                        m.Identifier.Text,
                        m,
                        m.Identifier.GetLocation());
                    break;

                case ClassDeclarationSyntax c:
                    yield return new SymbolInfo(
                        SymbolKind.Class,
                        AccessibilityFlags(c.Modifiers, DefaultTypeAccessibility(c)) | TypeModifierFlags(c.Modifiers),
                        c.Identifier.Text,
                        c,
                        c.Identifier.GetLocation());
                    break;

                case InterfaceDeclarationSyntax i:
                    yield return new SymbolInfo(
                        SymbolKind.Interface,
                        AccessibilityFlags(i.Modifiers, DefaultTypeAccessibility(i)),
                        i.Identifier.Text,
                        i,
                        i.Identifier.GetLocation());
                    break;

                case RecordDeclarationSyntax r:
                    yield return new SymbolInfo(
                        SymbolKind.Record,
                        AccessibilityFlags(r.Modifiers, DefaultTypeAccessibility(r)) | TypeModifierFlags(r.Modifiers),
                        r.Identifier.Text,
                        r,
                        r.Identifier.GetLocation());
                    break;

                case EnumDeclarationSyntax e:
                    yield return new SymbolInfo(
                        SymbolKind.Enum,
                        AccessibilityFlags(e.Modifiers, DefaultTypeAccessibility(e)),
                        e.Identifier.Text,
                        e,
                        e.Identifier.GetLocation());
                    break;

                case PropertyDeclarationSyntax p:
                    {
                        var flags = AccessibilityFlags(p.Modifiers, DefaultMemberAccessibility(p));
                        if (p.Modifiers.Any(SyntaxKind.StaticKeyword)) flags |= PredicateFlags.Static;
                        yield return new SymbolInfo(
                            SymbolKind.Property,
                            flags,
                            p.Identifier.Text,
                            p,
                            p.Identifier.GetLocation());
                        break;
                    }

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

    // Unqualified members default to private, except interface members which are implicitly public.
    private static PredicateFlags DefaultMemberAccessibility(SyntaxNode member) =>
        member.Parent is InterfaceDeclarationSyntax ? PredicateFlags.Public : PredicateFlags.Private;

    // Unqualified types default to internal at namespace/file scope, private when nested in another
    // type, and public when nested in an interface (matching C#'s member accessibility rules).
    private static PredicateFlags DefaultTypeAccessibility(SyntaxNode type) => type.Parent switch
    {
        InterfaceDeclarationSyntax => PredicateFlags.Public,
        TypeDeclarationSyntax => PredicateFlags.Private,
        _ => PredicateFlags.Internal,
    };

    private static PredicateFlags TypeModifierFlags(SyntaxTokenList modifiers)
    {
        var flags = PredicateFlags.None;
        if (modifiers.Any(SyntaxKind.StaticKeyword)) flags |= PredicateFlags.Static;
        if (modifiers.Any(SyntaxKind.AbstractKeyword)) flags |= PredicateFlags.Abstract;
        if (modifiers.Any(SyntaxKind.SealedKeyword)) flags |= PredicateFlags.Sealed;
        return flags;
    }

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
