using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows a <c>Fields(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all fields. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class FieldPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> fields.</summary>
    public FieldPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>private</c> fields.</summary>
    public FieldPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }

    /// <summary>Restricts the rule to <c>const</c> fields.</summary>
    public FieldPredicateBuilder Const() { Flags |= PredicateFlags.Const; return this; }
}
