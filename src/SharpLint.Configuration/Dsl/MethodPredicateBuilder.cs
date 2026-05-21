using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

public sealed class MethodPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    public MethodPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }
    public MethodPredicateBuilder Internal() { Flags |= PredicateFlags.Internal; return this; }
    public MethodPredicateBuilder Protected() { Flags |= PredicateFlags.Protected; return this; }
    public MethodPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }
    public MethodPredicateBuilder Async() { Flags |= PredicateFlags.Async; return this; }
}
