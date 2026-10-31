using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

/// <summary>
/// Narrows a <c>Records(...)</c> selector. Combine filters to target a subset; with none,
/// the rule applies to all records. Multiple filters must all hold (logical AND).
/// </summary>
public sealed class RecordPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    /// <summary>Restricts the rule to <c>public</c> records.</summary>
    public RecordPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }

    /// <summary>Restricts the rule to <c>internal</c> records.</summary>
    public RecordPredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }

    /// <summary>Restricts the rule to <c>protected</c> records (nested).</summary>
    public RecordPredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }

    /// <summary>Restricts the rule to <c>private</c> records (nested).</summary>
    public RecordPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }

    /// <summary>Restricts the rule to <c>abstract</c> records.</summary>
    public RecordPredicateBuilder Abstract() { Flags |= PredicateFlags.Abstract; return this; }

    /// <summary>Restricts the rule to <c>sealed</c> records.</summary>
    public RecordPredicateBuilder Sealed() { Flags |= PredicateFlags.Sealed; return this; }
}
