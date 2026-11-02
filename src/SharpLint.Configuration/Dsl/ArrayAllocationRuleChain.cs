namespace SharpLint.Configuration.Dsl;

/// <summary>Requirements for explicit array allocations; modifiers affect the most recent requirement.</summary>
public sealed class ArrayAllocationRuleChain : BuiltInRuleChain<ArrayAllocationRuleChain>
{
    internal ArrayAllocationRuleChain(ConfigBuilder configuration) : base(configuration) { }

    /// <summary>Forbids explicit array allocations with a literal zero length.</summary>
    public ArrayAllocationRuleChain ForbidEmptyAllocation() => Add("performance.empty-array-allocation");
}
