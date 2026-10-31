using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows an <c>Enums(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all enums. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class EnumPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> enums.</summary>
    public EnumPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>internal</c> enums.</summary>
    public EnumPredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }

    /// <summary>Restricts the rule to <c>protected</c> enums (nested).</summary>
    public EnumPredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }

    /// <summary>Restricts the rule to <c>private</c> enums (nested).</summary>
    public EnumPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }
}
