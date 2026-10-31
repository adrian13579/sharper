using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows an <c>Interfaces(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all interfaces. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class InterfacePredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> interfaces.</summary>
    public InterfacePredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>internal</c> interfaces.</summary>
    public InterfacePredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }

    /// <summary>Restricts the rule to <c>protected</c> interfaces (nested).</summary>
    public InterfacePredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }

    /// <summary>Restricts the rule to <c>private</c> interfaces (nested).</summary>
    public InterfacePredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }
}
