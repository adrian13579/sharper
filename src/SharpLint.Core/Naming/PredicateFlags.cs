namespace SharpLint.Core.Naming;

[Flags]
public enum PredicateFlags
{
    None = 0,
    Public = 1 << 0,
    Internal = 1 << 1,
    Protected = 1 << 2,
    Private = 1 << 3,
    Async = 1 << 4,
    Const = 1 << 5,
    Static = 1 << 6,
    Abstract = 1 << 7,
    Sealed = 1 << 8,
}
