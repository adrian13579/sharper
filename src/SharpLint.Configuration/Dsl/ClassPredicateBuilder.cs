using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows a <c>Classes(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all classes. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class ClassPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> classes.</summary>
    public ClassPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>internal</c> classes.</summary>
    public ClassPredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }

    /// <summary>Restricts the rule to <c>protected</c> classes (nested).</summary>
    public ClassPredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }

    /// <summary>Restricts the rule to <c>private</c> classes (nested).</summary>
    public ClassPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }

    /// <summary>Restricts the rule to <c>static</c> classes.</summary>
    public ClassPredicateBuilder Static() { Flags |= PredicateFlags.Static; return this; }

    /// <summary>Restricts the rule to <c>abstract</c> classes.</summary>
    public ClassPredicateBuilder Abstract() { Flags |= PredicateFlags.Abstract; return this; }

    /// <summary>Restricts the rule to <c>sealed</c> classes.</summary>
    public ClassPredicateBuilder Sealed() { Flags |= PredicateFlags.Sealed; return this; }
}
