using SharpLint.Configuration;
using SharpLint.Configuration.Dsl;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Tests.Analysis;

public sealed class AnalyzerTests : IDisposable
{
    private readonly string _workDir;

    public AnalyzerTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"sharplint-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private void WriteFile(string name, string content) =>
        File.WriteAllText(Path.Combine(_workDir, name), content);

    private async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(Action<ConfigBuilder> configure)
    {
        var config = Lint.Configure(configure);
        var result = await new Analyzer().AnalyzeAsync(new AnalysisContext
        {
            WorkingDirectory = _workDir,
            NamingRules = config.NamingRules,
        });
        return result.Diagnostics;
    }

    [Fact]
    public async Task Interface_prefix_required()
    {
        WriteFile("Foo.cs", "public interface Foo { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Interfaces().RequirePrefix("I")));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.interface.prefix", diag.RuleId);
    }

    [Fact]
    public async Task Async_method_missing_suffix_flagged()
    {
        WriteFile("Foo.cs", """
            using System.Threading.Tasks;
            class C { public async Task DoWork() { await Task.Yield(); } }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Methods(m => m.Async()).RequireSuffix("Async")));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.method.async.suffix", diag.RuleId);
    }

    [Fact]
    public async Task Class_pascal_case_required_pass_and_fail()
    {
        WriteFile("A.cs", "class fooBar { }");
        WriteFile("B.cs", "class FooBar { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes().RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.class.convention", diag.RuleId);
        Assert.Contains("fooBar", diag.Message);
    }

    [Fact]
    public async Task Private_field_chain_requires_prefix_and_camel_case()
    {
        WriteFile("F.cs", """
            class C
            {
                private int FooBar;
                private int _fooBar;
                private int _FooBar;
            }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Fields(f => f.Private())
                .RequirePrefix("_")
                .RequireConvention(NamingConvention.CamelCase)));

        Assert.Contains(diags, d => d.RuleId == "naming.field.private.prefix");
        Assert.Contains(diags, d => d.RuleId == "naming.field.private.convention");
    }

    [Fact]
    public async Task Severity_modifier_applies_to_last_rule()
    {
        WriteFile("A.cs", "class fooBar { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes().RequireConvention(NamingConvention.PascalCase).AsError()));

        var diag = Assert.Single(diags);
        Assert.Equal(Severity.Error, diag.Severity);
    }

    [Fact]
    public async Task Disabled_rule_emits_nothing()
    {
        WriteFile("A.cs", "class fooBar { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes().RequireConvention(NamingConvention.PascalCase).Disabled()));

        Assert.Empty(diags);
    }

    [Fact]
    public async Task ExceptWithAttribute_skips_matching_symbol()
    {
        WriteFile("A.cs", """
            using System.CodeDom.Compiler;
            [GeneratedCode("tool","1.0")]
            class fooBar { }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes()
                .RequireConvention(NamingConvention.PascalCase)
                .ExceptWithAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>()));

        Assert.Empty(diags);
    }

    [Fact]
    public async Task ForbidSuffix_flags_classes_with_listed_suffix()
    {
        WriteFile("A.cs", "class FooManager { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes().ForbidSuffix("Manager", "Helper", "Util")));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.class.suffix", diag.RuleId);
    }

    [Fact]
    public async Task Regex_verb_anchors_pattern()
    {
        WriteFile("A.cs", "class C { private void notLikeThis() { } }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Methods(m => m.Private()).RequireRegex("_?[a-z][A-Za-z0-9]*")));

        Assert.Empty(diags);
    }

    [Fact]
    public async Task Namespace_prefix_required()
    {
        WriteFile("A.cs", """
            namespace Acme.Foo { class C { } }
            """);
        WriteFile("B.cs", """
            namespace Other { class D { } }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Namespaces().RequirePrefix("Acme.")));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.namespace.prefix", diag.RuleId);
    }

    [Fact]
    public async Task Later_general_rule_overrides_earlier_specific_rule()
    {
        // A later, broader rule wins over an earlier, more specific one (e.g. a preset's public rule).
        WriteFile("C.cs", """
            class C
            {
                public void DoWork() { }
                public void helper() { }
            }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
        {
            n.Methods(m => m.Public()).RequireConvention(NamingConvention.CamelCase); // earlier, specific
            n.Methods().RequireConvention(NamingConvention.PascalCase);               // later, general → wins
        }));

        // The general PascalCase rule governs public methods now: 'DoWork' passes, 'helper' fails.
        // The earlier public-camelCase rule is fully overridden (it would have flagged 'DoWork').
        var diag = Assert.Single(diags);
        Assert.Equal("naming.method.convention", diag.RuleId);
        Assert.Contains("helper", diag.Message);
    }

    [Fact]
    public async Task Later_specific_rule_overrides_earlier_general_rule()
    {
        // Declaring the specific rule last lets it win for the scope it covers, while the general
        // rule still governs everything the specific rule does not match.
        // 'Do_Work' violates *both* PascalCase and camelCase, so the old "every rule fires" behavior
        // would flag it twice (general + public). With resolution, only the winning public rule fires.
        WriteFile("C.cs", """
            class C
            {
                public void Do_Work() { }
                private void helper() { }
            }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
        {
            n.Methods().RequireConvention(NamingConvention.PascalCase);               // earlier, general
            n.Methods(m => m.Public()).RequireConvention(NamingConvention.CamelCase); // later, specific → wins for public
        }));

        Assert.Equal(2, diags.Count);
        Assert.Contains(diags, d => d.RuleId == "naming.method.public.convention" && d.Message.Contains("Do_Work"));
        Assert.Contains(diags, d => d.RuleId == "naming.method.convention" && d.Message.Contains("helper"));
    }

    [Fact]
    public async Task Later_disabled_rule_shadows_earlier_active_rule()
    {
        // Re-declaring a rule and calling .Disabled() switches off an earlier (e.g. preset) rule.
        WriteFile("A.cs", "class fooBar { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
        {
            n.Classes().RequireConvention(NamingConvention.PascalCase);            // earlier, active
            n.Classes().RequireConvention(NamingConvention.PascalCase).Disabled(); // later, disables it
        }));

        Assert.Empty(diags);
    }

    [Fact]
    public async Task Later_rule_overrides_earlier_rule_with_same_scope()
    {
        // Two rules with the same scope + verb (both naming.class.convention): the later declaration wins.
        WriteFile("A.cs", "public class fooBar { }");

        var diags = await AnalyzeAsync(c => c.Naming(n =>
        {
            n.Classes().RequireConvention(NamingConvention.UpperSnakeCase); // earlier, active
            n.Classes().RequireConvention(NamingConvention.PascalCase); 
        }));

        Assert.Contains(diags, d => d.RuleId == "naming.class.convention" && d.Message.Contains("fooBar"));
    }
}
