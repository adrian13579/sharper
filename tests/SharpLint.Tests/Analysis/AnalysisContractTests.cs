using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Tests.Analysis;

public sealed class AnalysisContractTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharplint-contract-{Guid.NewGuid():N}");

    public AnalysisContractTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task<AnalysisResult> Analyze(string source, params NamingRule[] rules)
    {
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), source);
        return await new Analyzer().AnalyzeAsync(new AnalysisContext { WorkingDirectory = _directory, NamingRules = rules });
    }

    [Theory]
    [InlineData(VerbKind.Convention, Direction.Require, "lower", "Class 'lower' must use PascalCase convention.")]
    [InlineData(VerbKind.Convention, Direction.Forbid, "Upper", "Class 'Upper' must not use PascalCase convention.")]
    [InlineData(VerbKind.Prefix, Direction.Require, "Other", "Class 'Other' must start with any of ['Pre', 'Alt'].")]
    [InlineData(VerbKind.Prefix, Direction.Forbid, "PreName", "Class 'PreName' must not start with any of ['Pre', 'Alt'].")]
    [InlineData(VerbKind.Suffix, Direction.Require, "Other", "Class 'Other' must end with any of ['Pre', 'Alt'].")]
    [InlineData(VerbKind.Suffix, Direction.Forbid, "NameAlt", "Class 'NameAlt' must not end with any of ['Pre', 'Alt'].")]
    [InlineData(VerbKind.Name, Direction.Require, "Other", "Class 'Other' must be named any of ['Pre', 'Alt'].")]
    [InlineData(VerbKind.Name, Direction.Forbid, "Alt", "Class 'Alt' must not be named any of ['Pre', 'Alt'].")]
    [InlineData(VerbKind.Regex, Direction.Require, "lower", "Class 'lower' must match pattern '[A-Z]+'.")]
    [InlineData(VerbKind.Regex, Direction.Forbid, "UPPER", "Class 'UPPER' must not match pattern '[A-Z]+'.")]
    public async Task Diagnostics_describe_the_constraint_and_exact_source_location(
        VerbKind verb, Direction direction, string name, string message)
    {
        var result = await Analyze($"// heading\n  class {name} {{ }}", new NamingRule
        {
            Kind = SymbolKind.Class, Verb = verb, Direction = direction,
            Convention = NamingConvention.PascalCase, Values = ["Pre", "Alt"], Pattern = "[A-Z]+", Severity = Severity.Error,
        });
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(message, diagnostic.Message);
        Assert.Equal($"naming.class.{verb.ToString().ToLowerInvariant()}", diagnostic.RuleId);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal(new SourceLocation(Path.Combine(_directory, "Sample.cs"), 2, 9), diagnostic.Location);
        Assert.Null(diagnostic.Suggestion);
        Assert.True(result.HasErrors);
        Assert.Equal(1, result.FilesAnalyzed);
        Assert.True(result.Elapsed >= TimeSpan.Zero);
    }

    [Theory]
    [InlineData(VerbKind.Prefix, "PreName")]
    [InlineData(VerbKind.Prefix, "AltName")]
    [InlineData(VerbKind.Suffix, "NamePre")]
    [InlineData(VerbKind.Suffix, "NameAlt")]
    [InlineData(VerbKind.Name, "Pre")]
    [InlineData(VerbKind.Name, "Alt")]
    public async Task Any_listed_value_can_satisfy_a_requirement(VerbKind verb, string name)
    {
        var result = await Analyze($"class {name} {{ }}", new NamingRule { Kind = SymbolKind.Class, Verb = verb, Values = ["Pre", "Alt"] });
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("[A-Z]+", "xUPPER", true)]
    [InlineData("[A-Z]+", "UPPERx", true)]
    [InlineData("^[A-Z]+$", "UPPER", false)]
    [InlineData("^[A-Z]+", "UPPERx", true)]
    [InlineData("[A-Z]+$", "xUPPER", true)]
    public async Task Regex_matches_the_entire_name(string pattern, string name, bool violation)
    {
        var result = await Analyze($"class {name} {{ }}", new NamingRule { Kind = SymbolKind.Class, Verb = VerbKind.Regex, Pattern = pattern });
        Assert.Equal(violation ? 1 : 0, result.Diagnostics.Count);
    }

    [Fact]
    public async Task Namespace_conventions_validate_each_segment_for_both_namespace_forms()
    {
        var rule = new NamingRule { Kind = SymbolKind.Namespace, Verb = VerbKind.Convention, Convention = NamingConvention.PascalCase };
        Assert.Empty((await Analyze("namespace Acme.Tools; class C {}", rule)).Diagnostics);
        Assert.Single((await Analyze("namespace Acme.lower { class C {} }", rule)).Diagnostics);
        Assert.Single((await Analyze("namespace lower.Tools; class C {}", rule)).Diagnostics);
    }

    [Theory]
    [InlineData("Obsolete")]
    [InlineData("ObsoleteAttribute")]
    [InlineData("System.Obsolete")]
    [InlineData("System.ObsoleteAttribute")]
    public async Task Attribute_exemptions_accept_qualified_and_short_names(string attribute)
    {
        var rule = new NamingRule { Kind = SymbolKind.Class, Verb = VerbKind.Prefix, Values = ["X"], ExemptAttributes = ["System.ObsoleteAttribute"] };
        var result = await Analyze($"[{attribute}] class Exempt {{}} [Serializable] class Reported {{}} class Plain {{}}", rule);
        Assert.Equal(new[] { "Class 'Reported' must start with 'X'.", "Class 'Plain' must start with 'X'." }, result.Diagnostics.Select(d => d.Message));
    }

    [Fact]
    public async Task Exemptions_support_parameters_locals_and_unattributed_namespaces()
    {
        var rules = new[] { SymbolKind.Parameter, SymbolKind.LocalVariable, SymbolKind.Namespace }.Select(kind => new NamingRule
        {
            Kind = kind, Verb = VerbKind.Prefix, Values = ["X"], ExemptAttributes = ["Obsolete"],
        }).ToArray();
        var result = await Analyze("namespace Acme; class C { void M([System.Obsolete] int skipped, int reported) { [Obsolete] int exempt = 1; int local = 2; } }", rules);
        Assert.Equal(new[] { "naming.namespace.prefix", "naming.parameter.prefix", "naming.local.prefix" }, result.Diagnostics.Select(d => d.RuleId));
    }

    [Fact]
    public async Task Analyzer_counts_only_source_files_outside_build_directories()
    {
        foreach (var path in new[] { "bin", "obj", "nested/bin", "nested/obj", "nested" })
        {
            Directory.CreateDirectory(Path.Combine(_directory, path));
            File.WriteAllText(Path.Combine(_directory, path, "Other.cs"), "class bad {}");
        }
        File.WriteAllText(Path.Combine(_directory, "Ignore.txt"), "class bad {}");
        var result = await Analyze("class bad {}", new NamingRule { Kind = SymbolKind.Class, Verb = VerbKind.Convention, Convention = NamingConvention.PascalCase });
        Assert.Equal(2, result.FilesAnalyzed);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void HasErrors_requires_at_least_one_error_and_allows_mixed_severities()
    {
        Diagnostic Create(Severity severity) => new("id", severity, "message", new SourceLocation("a.cs", 1, 1));
        Assert.False(new AnalysisResult([], TimeSpan.Zero, 0).HasErrors);
        Assert.False(new AnalysisResult([Create(Severity.Warning), Create(Severity.Info)], TimeSpan.Zero, 1).HasErrors);
        Assert.True(new AnalysisResult([Create(Severity.Warning), Create(Severity.Error)], TimeSpan.Zero, 1).HasErrors);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), "class C {}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Analyzer().AnalyzeAsync(
            new AnalysisContext { WorkingDirectory = _directory, NamingRules = [] }, cancellation.Token));
    }

    [Fact]
    public async Task Incomplete_parameter_declarations_do_not_produce_empty_name_diagnostics()
    {
        var result = await Analyze("class C { void M(int) {} }", new NamingRule
        {
            Kind = SymbolKind.Parameter, Verb = VerbKind.Convention, Convention = NamingConvention.CamelCase,
        });
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Cancellation_during_rule_resolution_stops_before_the_next_symbol()
    {
        File.WriteAllText(Path.Combine(_directory, "Sample.cs"), "class First {} class Second {}");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Analyzer().AnalyzeAsync(
            new AnalysisContext { WorkingDirectory = _directory, NamingRules = new CancelDuringEnumeration(cancellation) },
            cancellation.Token));
    }

    private sealed class CancelDuringEnumeration(CancellationTokenSource cancellation) : IReadOnlyList<NamingRule>
    {
        public int Count => 0;
        public NamingRule this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<NamingRule> GetEnumerator()
        {
            cancellation.Cancel();
            return Enumerable.Empty<NamingRule>().GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static IEnumerable<object[]> SymbolCases()
    {
        yield return new object[] { "class C { internal void bad() {} public void Other() {} }", SymbolKind.Method, PredicateFlags.Internal, "bad" };
        yield return new object[] { "class C { protected void bad() {} private void Other() {} }", SymbolKind.Method, PredicateFlags.Protected, "bad" };
        yield return new object[] { "class C { protected internal void bad() {} internal void Other() {} }", SymbolKind.Method, PredicateFlags.Protected | PredicateFlags.Internal, "bad" };
        yield return new object[] { "class C { async void bad() {} void other() {} }", SymbolKind.Method, PredicateFlags.Async, "bad" };
        yield return new object[] { "interface C { void bad(); private void Other() {} }", SymbolKind.Method, PredicateFlags.Public, "bad" };
        yield return new object[] { "interface C { int bad { get; } private int Other { get; } }", SymbolKind.Property, PredicateFlags.Public, "bad" };
        yield return new object[] { "class C { int bad { get; } public int Other { get; } }", SymbolKind.Property, PredicateFlags.Private, "bad" };
        yield return new object[] { "class C { const int bad = 1; int Other; }", SymbolKind.Field, PredicateFlags.Private | PredicateFlags.Const, "bad" };
        yield return new object[] { "class C { public const int bad = 1; public int Other; }", SymbolKind.Field, PredicateFlags.Public | PredicateFlags.Const, "bad" };
        yield return new object[] { "internal class bad {} public class Other {}", SymbolKind.Class, PredicateFlags.Internal, "bad" };
        yield return new object[] { "sealed class bad {} class Other {}", SymbolKind.Class, PredicateFlags.Sealed, "bad" };
        yield return new object[] { "interface Outer { class bad {} private class Other {} }", SymbolKind.Class, PredicateFlags.Public, "bad" };
        yield return new object[] { "class C { void M(int bad, int Other) {} }", SymbolKind.Parameter, PredicateFlags.None, "bad" };
        yield return new object[] { "class C { void M() { int bad = 1, Other = 2; } }", SymbolKind.LocalVariable, PredicateFlags.None, "bad" };
    }

    [Theory]
    [MemberData(nameof(SymbolCases))]
    public async Task Selectors_report_only_matching_symbols(string source, SymbolKind kind, PredicateFlags predicates, string name)
    {
        var result = await Analyze(source, new NamingRule { Kind = kind, Predicates = predicates, Verb = VerbKind.Convention, Convention = NamingConvention.PascalCase });
        Assert.Contains($"'{name}'", Assert.Single(result.Diagnostics).Message);
    }
}
