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
    public async Task Abstract_class_predicate_targets_only_abstract_classes()
    {
        WriteFile("A.cs", """
            abstract class Widget { }
            class HelperWidget { }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes(c => c.Abstract()).RequirePrefix("Abstract", "Base")));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.class.abstract.prefix", diag.RuleId);
        Assert.Contains("Widget", diag.Message);
    }

    [Fact]
    public async Task Static_class_predicate_targets_only_static_classes()
    {
        WriteFile("A.cs", """
            static class helpers { }
            class Widget { }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes(c => c.Static()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.class.static.convention", diag.RuleId);
        Assert.Contains("helpers", diag.Message);
    }

    [Fact]
    public async Task Sealed_record_predicate_targets_only_sealed_records()
    {
        WriteFile("A.cs", """
            public sealed record fooRecord(int X);
            public record BarRecord(int Y);
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Records(r => r.Sealed()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.record.sealed.convention", diag.RuleId);
        Assert.Contains("fooRecord", diag.Message);
    }

    [Fact]
    public async Task Static_property_predicate_targets_only_static_properties()
    {
        WriteFile("A.cs", """
            class C
            {
                public static int currentCount { get; set; }
                public int Name { get; set; }
            }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Properties(p => p.Static()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.property.static.convention", diag.RuleId);
        Assert.Contains("currentCount", diag.Message);
    }

    [Fact]
    public async Task Interface_public_predicate_targets_only_public_interfaces()
    {
        WriteFile("A.cs", """
            public interface foo { }
            internal interface bar { }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Interfaces(i => i.Public()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.interface.public.convention", diag.RuleId);
        Assert.Contains("foo", diag.Message);
    }

    [Fact]
    public async Task Enum_public_predicate_targets_only_public_enums()
    {
        WriteFile("A.cs", """
            public enum colors { Red }
            enum shapes { Square }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Enums(e => e.Public()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.enum.public.convention", diag.RuleId);
        Assert.Contains("colors", diag.Message);
    }

    [Fact]
    public async Task Nested_type_defaults_to_private_accessibility()
    {
        // 'inner' has no accessibility modifier; nested in a class it defaults to private,
        // so a Private() predicate matches it but not the public top-level type.
        WriteFile("A.cs", """
            public class Outer
            {
                class inner { }
            }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes(c => c.Private()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.class.private.convention", diag.RuleId);
        Assert.Contains("inner", diag.Message);
    }

    [Fact]
    public async Task Class_combined_public_abstract_predicate_ands()
    {
        // Only the class that is public AND abstract is matched.
        WriteFile("A.cs", """
            public abstract class fooBase { }
            internal abstract class barBase { }
            public class bazBase { }
            """);

        var diags = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes(c => c.Public().Abstract()).RequireConvention(NamingConvention.PascalCase)));

        var diag = Assert.Single(diags);
        Assert.Equal("naming.class.public.abstract.convention", diag.RuleId);
        Assert.Contains("fooBase", diag.Message);
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

    [Theory]
    [InlineData(VerbKind.Convention, "GoodName", "lower", "UPPER")]
    [InlineData(VerbKind.Prefix, "Good", "Other", "Bad")]
    [InlineData(VerbKind.Suffix, "GoodEnd", "Other", "BadEnd")]
    [InlineData(VerbKind.Name, "Good", "Other", "Bad")]
    [InlineData(VerbKind.Regex, "Good", "Other", "Bad")]
    public async Task Chained_require_and_forbid_of_the_same_verb_both_apply(
        VerbKind verb, string compliant, string missingRequirement, string forbidden)
    {
        WriteFile("Sample.cs", $"class {compliant} {{}} class {missingRequirement} {{}} class {forbidden} {{}}");
        var diagnostics = await AnalyzeAsync(c => c.Naming(n =>
        {
            var chain = n.Classes();
            switch (verb)
            {
                case VerbKind.Convention:
                    chain.RequireConvention(NamingConvention.PascalCase).ForbidConvention(NamingConvention.UpperSnakeCase);
                    break;
                case VerbKind.Prefix:
                    chain.RequirePrefix("G", "B").ForbidPrefix("B");
                    break;
                case VerbKind.Suffix:
                    chain.RequireSuffix("End").ForbidSuffix("BadEnd");
                    break;
                case VerbKind.Name:
                    chain.RequireName("Good", "Bad").ForbidName("Bad");
                    break;
                case VerbKind.Regex:
                    chain.RequireRegex("(Good|Bad)").ForbidRegex("Bad");
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(verb));
            }
        }));
        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, d => d.Message.Contains($"'{missingRequirement}' must "));
        Assert.Contains(diagnostics, d => d.Message.Contains($"'{forbidden}' must not "));
        Assert.All(diagnostics, d => Assert.Equal($"naming.class.{verb.ToString().ToLowerInvariant()}", d.RuleId));
    }

    [Fact]
    public async Task Repeated_require_constraints_keep_individual_severities_and_exemptions()
    {
        WriteFile("Sample.cs", """
            class Neither {}
            class IOnly {}
            class IGood {}
            [Obsolete] class Exempt {}
            """);
        var diagnostics = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes().RequirePrefix("I").AsError().ExceptWithAttribute<ObsoleteAttribute>()
                .RequirePrefix("IG").AsInfo()));
        Assert.Equal(4, diagnostics.Count);
        Assert.Contains(diagnostics, d => d.Message == "Class 'Neither' must start with 'I'." && d.Severity == Severity.Error);
        Assert.Contains(diagnostics, d => d.Message == "Class 'Neither' must start with 'IG'." && d.Severity == Severity.Info);
        Assert.Contains(diagnostics, d => d.Message == "Class 'IOnly' must start with 'IG'." && d.Severity == Severity.Info);
        Assert.Contains(diagnostics, d => d.Message == "Class 'Exempt' must start with 'IG'." && d.Severity == Severity.Info);
    }

    [Fact]
    public async Task Repeated_forbid_constraints_all_apply_and_can_report_two_violations()
    {
        WriteFile("Sample.cs", "class Good {} class IOnly {} class IBad {}");
        var diagnostics = await AnalyzeAsync(c => c.Naming(n =>
            n.Classes().ForbidPrefix("I").ForbidPrefix("IBad")));
        Assert.Equal(new[]
        {
            "Class 'IOnly' must not start with 'I'.",
            "Class 'IBad' must not start with 'I'.",
            "Class 'IBad' must not start with 'IBad'.",
        }, diagnostics.Select(d => d.Message));
    }

    [Fact]
    public async Task Later_selector_replaces_all_earlier_constraints_of_that_verb_only()
    {
        WriteFile("Sample.cs", "public class Good {} private class IBad {}");
        var diagnostics = await AnalyzeAsync(c => c.Naming(n =>
        {
            n.Classes().RequirePrefix("I").ForbidPrefix("IBad").RequireSuffix("End");
            n.Classes(p => p.Public()).RequirePrefix("G").ForbidPrefix("Good");
        }));
        Assert.Equal(new[]
        {
            "Class 'Good' must not start with 'Good'.",
            "Class 'Good' must end with 'End'.",
            "Class 'IBad' must not start with 'IBad'.",
            "Class 'IBad' must end with 'End'.",
        }, diagnostics.Select(d => d.Message));
    }

    [Fact]
    public async Task Disabled_constraint_does_not_disable_active_siblings_or_revive_earlier_chains()
    {
        WriteFile("Sample.cs", "class Other {} class IBad {}");
        var diagnostics = await AnalyzeAsync(c => c.Naming(n =>
        {
            n.Classes().RequirePrefix("X");
            n.Classes().RequirePrefix("I").ForbidPrefix("IBad").Disabled();
        }));
        Assert.Equal("Class 'Other' must start with 'I'.", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public async Task Reusing_an_earlier_chain_makes_its_entire_verb_group_win_again()
    {
        WriteFile("Sample.cs", "class IGood {} class Other {} class IBad {}");
        var diagnostics = await AnalyzeAsync(c => c.Naming(n =>
        {
            var original = n.Classes().RequirePrefix("I");
            n.Classes().RequirePrefix("X");
            original.ForbidPrefix("IBad");
        }));
        Assert.Equal(new[] { "Class 'Other' must start with 'I'.", "Class 'IBad' must not start with 'IBad'." },
            diagnostics.Select(d => d.Message));
    }
}
