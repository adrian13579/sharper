using SharpLint.Core.Naming;

namespace SharpLint.Configuration.Dsl;

public sealed class FieldPredicateBuilder
{
    internal PredicateFlags Flags { get; private set; }

    public FieldPredicateBuilder Public() { Flags |= PredicateFlags.Public; return this; }
    public FieldPredicateBuilder Private() { Flags |= PredicateFlags.Private; return this; }
    public FieldPredicateBuilder Const() { Flags |= PredicateFlags.Const; return this; }
}
