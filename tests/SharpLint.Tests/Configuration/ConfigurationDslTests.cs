using SharpLint.Configuration;
using SharpLint.Configuration.Dsl;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;

namespace SharpLint.Tests.Configuration;

public sealed class ConfigurationDslTests
{
    public static IEnumerable<object[]> PredicateCases()
    {
        yield return new object[] { SymbolKind.Method, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Methods(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Method, PredicateFlags.Internal, (Action<NamingBuilder>)(n => n.Methods(p => p.Internal().Internal()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Method, PredicateFlags.Protected, (Action<NamingBuilder>)(n => n.Methods(p => p.Protected().Protected()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Method, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Methods(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Method, PredicateFlags.Async, (Action<NamingBuilder>)(n => n.Methods(p => p.Async().Async()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Field, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Fields(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Field, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Fields(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Field, PredicateFlags.Const, (Action<NamingBuilder>)(n => n.Fields(p => p.Const().Const()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Classes(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Internal, (Action<NamingBuilder>)(n => n.Classes(p => p.Internal().Internal()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Protected, (Action<NamingBuilder>)(n => n.Classes(p => p.Protected().Protected()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Classes(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Static, (Action<NamingBuilder>)(n => n.Classes(p => p.Static().Static()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Abstract, (Action<NamingBuilder>)(n => n.Classes(p => p.Abstract().Abstract()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Class, PredicateFlags.Sealed, (Action<NamingBuilder>)(n => n.Classes(p => p.Sealed().Sealed()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Interface, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Interfaces(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Interface, PredicateFlags.Internal, (Action<NamingBuilder>)(n => n.Interfaces(p => p.Internal().Internal()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Interface, PredicateFlags.Protected, (Action<NamingBuilder>)(n => n.Interfaces(p => p.Protected().Protected()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Interface, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Interfaces(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Record, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Records(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Record, PredicateFlags.Internal, (Action<NamingBuilder>)(n => n.Records(p => p.Internal().Internal()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Record, PredicateFlags.Protected, (Action<NamingBuilder>)(n => n.Records(p => p.Protected().Protected()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Record, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Records(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Record, PredicateFlags.Abstract, (Action<NamingBuilder>)(n => n.Records(p => p.Abstract().Abstract()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Record, PredicateFlags.Sealed, (Action<NamingBuilder>)(n => n.Records(p => p.Sealed().Sealed()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Enum, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Enums(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Enum, PredicateFlags.Internal, (Action<NamingBuilder>)(n => n.Enums(p => p.Internal().Internal()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Enum, PredicateFlags.Protected, (Action<NamingBuilder>)(n => n.Enums(p => p.Protected().Protected()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Enum, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Enums(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Property, PredicateFlags.Public, (Action<NamingBuilder>)(n => n.Properties(p => p.Public().Public()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Property, PredicateFlags.Internal, (Action<NamingBuilder>)(n => n.Properties(p => p.Internal().Internal()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Property, PredicateFlags.Protected, (Action<NamingBuilder>)(n => n.Properties(p => p.Protected().Protected()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Property, PredicateFlags.Private, (Action<NamingBuilder>)(n => n.Properties(p => p.Private().Private()).RequirePrefix("X")) };
        yield return new object[] { SymbolKind.Property, PredicateFlags.Static, (Action<NamingBuilder>)(n => n.Properties(p => p.Static().Static()).RequirePrefix("X")) };
    }

    [Theory]
    [MemberData(nameof(PredicateCases))]
    public void Predicates_are_idempotent_and_preserve_the_selected_kind(
        SymbolKind kind, PredicateFlags predicate, Action<NamingBuilder> configure)
    {
        var config = Lint.Configure(c => c.Naming(configure));
        var rule = Assert.Single(config.NamingRules);
        Assert.Equal(kind, rule.Kind);
        Assert.Equal(predicate, rule.Predicates);
        Assert.Equal(new[] { "X" }, rule.Values);
    }

    [Fact]
    public void Combined_predicates_accumulate_and_empty_callbacks_select_all()
    {
        var config = Lint.Configure(c => c.Naming(n =>
        {
            n.Methods(p => p.Public().Internal().Protected().Private().Async()).RequireName("combined");
            n.Methods(_ => { }).RequireName("all");
            n.Fields(p => p.Public().Private().Const()).RequireName("combined");
            n.Fields(_ => { }).RequireName("all");
            n.Classes(p => p.Public().Internal().Protected().Private().Static().Abstract().Sealed()).RequireName("combined");
            n.Classes(_ => { }).RequireName("all");
            n.Interfaces(p => p.Public().Internal().Protected().Private()).RequireName("combined");
            n.Interfaces(_ => { }).RequireName("all");
            n.Records(p => p.Public().Internal().Protected().Private().Abstract().Sealed()).RequireName("combined");
            n.Records(_ => { }).RequireName("all");
            n.Enums(p => p.Public().Internal().Protected().Private()).RequireName("combined");
            n.Enums(_ => { }).RequireName("all");
            n.Properties(p => p.Public().Internal().Protected().Private().Static()).RequireName("combined");
            n.Properties(_ => { }).RequireName("all");
        }));
        Assert.Equal(14, config.NamingRules.Count);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected | PredicateFlags.Private | PredicateFlags.Async, config.NamingRules[0].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[1].Predicates);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Private | PredicateFlags.Const, config.NamingRules[2].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[3].Predicates);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected | PredicateFlags.Private | PredicateFlags.Static | PredicateFlags.Abstract | PredicateFlags.Sealed, config.NamingRules[4].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[5].Predicates);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected | PredicateFlags.Private, config.NamingRules[6].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[7].Predicates);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected | PredicateFlags.Private | PredicateFlags.Abstract | PredicateFlags.Sealed, config.NamingRules[8].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[9].Predicates);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected | PredicateFlags.Private, config.NamingRules[10].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[11].Predicates);
        Assert.Equal(PredicateFlags.Public | PredicateFlags.Internal | PredicateFlags.Protected | PredicateFlags.Private | PredicateFlags.Static, config.NamingRules[12].Predicates);
        Assert.Equal(PredicateFlags.None, config.NamingRules[13].Predicates);
    }

    [Fact]
    public void Null_callbacks_are_rejected_with_argument_errors()
    {
        var builder = new ConfigBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.UsePreset(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Naming(null!));
        builder.Naming(n =>
        {
            Assert.Throws<ArgumentNullException>(() => n.Methods(null!));
            Assert.Throws<ArgumentNullException>(() => n.Fields(null!));
            Assert.Throws<ArgumentNullException>(() => n.Classes(null!));
            Assert.Throws<ArgumentNullException>(() => n.Interfaces(null!));
            Assert.Throws<ArgumentNullException>(() => n.Records(null!));
            Assert.Throws<ArgumentNullException>(() => n.Enums(null!));
            Assert.Throws<ArgumentNullException>(() => n.Properties(null!));
        });
    }

    [Fact]
    public void Verbs_preserve_values_direction_and_last_rule_modifiers()
    {
        var config = Lint.Configure(c => c.Naming(n =>
        {
            n.Classes()
                .RequireConvention(NamingConvention.PascalCase).AsError()
                .ForbidConvention(NamingConvention.SnakeCase).AsInfo()
                .RequirePrefix("A", "B").AsWarning()
                .ForbidPrefix("C", "D").Disabled()
                .RequireSuffix("E", "F")
                .ForbidSuffix("G", "H")
                .RequireName("I", "J")
                .ForbidName("K", "L")
                .RequireRegex("[A-Z]+").ExceptWithAttribute<ObsoleteAttribute>()
                .ExceptWithAttribute<SerializableAttribute>()
                .ForbidRegex("[a-z]+");
        }));
        var rules = config.NamingRules;
        Assert.Equal(10, rules.Count);
        var verbs = new[] { VerbKind.Convention, VerbKind.Prefix, VerbKind.Suffix, VerbKind.Name, VerbKind.Regex };
        for (var i = 0; i < rules.Count; i++)
        {
            Assert.Equal(SymbolKind.Class, rules[i].Kind);
            Assert.Equal(PredicateFlags.None, rules[i].Predicates);
            Assert.Equal(verbs[i / 2], rules[i].Verb);
            Assert.Equal(i % 2 == 0 ? Direction.Require : Direction.Forbid, rules[i].Direction);
            if (i < 2 || i >= 8) Assert.Empty(rules[i].Values);
        }
        Assert.Equal(NamingConvention.PascalCase, rules[0].Convention);
        Assert.Equal(NamingConvention.SnakeCase, rules[1].Convention);
        Assert.Equal(Severity.Error, rules[0].Severity);
        Assert.Equal(Severity.Info, rules[1].Severity);
        Assert.Equal(Severity.Warning, rules[2].Severity);
        Assert.Equal(Severity.Hidden, rules[3].Severity);
        for (var i = 2; i < 8; i++)
            Assert.Equal(new[] { ((char)('A' + (i - 2) * 2)).ToString(), ((char)('B' + (i - 2) * 2)).ToString() }, rules[i].Values);
        Assert.Equal("[A-Z]+", rules[8].Pattern);
        Assert.Equal("[a-z]+", rules[9].Pattern);
        Assert.Equal(new[] { typeof(ObsoleteAttribute).FullName!, typeof(SerializableAttribute).FullName! }, rules[8].ExemptAttributes);
        Assert.Empty(rules[9].ExemptAttributes);
    }

    [Fact]
    public void Modifiers_require_a_preceding_verb_even_when_other_selectors_have_rules()
    {
        Lint.Configure(c => c.Naming(n =>
        {
            n.Classes().RequirePrefix("A");
            n.Classes().RequireSuffix("B");
            var selector = n.Methods();
            Action[] modifiers = [() => selector.AsError(), () => selector.AsWarning(),
                () => selector.AsInfo(), () => selector.Disabled(),
                () => selector.ExceptWithAttribute<ObsoleteAttribute>()];
            foreach (var modifier in modifiers)
            {
                var error = Assert.Throws<InvalidOperationException>(modifier);
                Assert.Equal("A modifier was called before any Require…/Forbid… verb on this selector.", error.Message);
            }
        }));
    }

    [Fact]
    public void Preset_registers_the_recommended_contract_before_user_overrides()
    {
        var config = Lint.Configure(c =>
        {
            Assert.Same(c, c.UsePreset(Preset.Recommended));
            Assert.Same(c, c.Naming(n => n.Classes().RequirePrefix("Custom")));
        });
        Assert.Equal(12, config.NamingRules.Count);
        var expected = new (string Id, NamingConvention? Convention, string[] Values)[]
        {
            ("naming.class.convention", NamingConvention.PascalCase, []),
            ("naming.interface.prefix", null, ["I"]),
            ("naming.interface.convention", NamingConvention.PascalCase, []),
            ("naming.method.public.convention", NamingConvention.PascalCase, []),
            ("naming.method.async.suffix", null, ["Async"]),
            ("naming.property.convention", NamingConvention.PascalCase, []),
            ("naming.field.private.prefix", null, ["_"]),
            ("naming.field.private.convention", NamingConvention.CamelCase, []),
            ("naming.field.const.convention", NamingConvention.UpperSnakeCase, []),
            ("naming.parameter.convention", NamingConvention.CamelCase, []),
            ("naming.local.convention", NamingConvention.CamelCase, []),
            ("naming.class.prefix", null, ["Custom"]),
        };
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Id, config.NamingRules[i].RuleId);
            Assert.Equal(expected[i].Convention, config.NamingRules[i].Convention);
            Assert.Equal(expected[i].Values, config.NamingRules[i].Values);
            Assert.Equal(Direction.Require, config.NamingRules[i].Direction);
            Assert.Equal(Severity.Warning, config.NamingRules[i].Severity);
        }
    }
}
