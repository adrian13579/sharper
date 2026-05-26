using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows a <c>Methods(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all methods. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class MethodPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> methods.</summary>
    public MethodPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>internal</c> methods.</summary>
    public MethodPredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }

    /// <summary>Restricts the rule to <c>protected</c> methods.</summary>
    public MethodPredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }

    /// <summary>Restricts the rule to <c>private</c> methods.</summary>
    public MethodPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }

    /// <summary>Restricts the rule to <c>async</c> methods.</summary>
    public MethodPredicateBuilder Async() { Flags |= PredicateFlags.Async; return this; }
}
