using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows a <c>Properties(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all properties. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class PropertyPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> properties.</summary>
    public PropertyPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>internal</c> properties.</summary>
    public PropertyPredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }

    /// <summary>Restricts the rule to <c>protected</c> properties.</summary>
    public PropertyPredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }

    /// <summary>Restricts the rule to <c>private</c> properties.</summary>
    public PropertyPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }

    /// <summary>Restricts the rule to <c>static</c> properties.</summary>
    public PropertyPredicateBuilder Static() { Flags |= PredicateFlags.Static; return this; }
}
