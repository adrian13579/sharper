using System.Text.Json;
using SharpLint.Configuration;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Tests.Configuration;

public sealed class RuleConfigurationTests
{
    [Fact]
    public void Rule_options_outlive_their_json_document_and_builder_preserves_declaration_order()
    {
        ConfigBuilder? captured = null;
        SharpLintConfig config;
        using (var document = JsonDocument.Parse("{\"limit\":3}"))
        {
            config = Lint.Configure(c =>
            {
                captured = c;
                Assert.Same(c, c.Rule("example.first", document.RootElement, Severity.Info));
                c.Rule("example.second", document.RootElement);
            });
            captured!.Rule("example.third", document.RootElement, Severity.Error);
        }
        Assert.Equal(new[] { "example.first", "example.second" }, config.Rules!.Select(r => r.RuleId));
        Assert.Equal(3, config.Rules![0].Options.GetProperty("limit").GetInt32());
        Assert.Equal(Severity.Info, config.Rules![0].Severity);
        Assert.Equal(Severity.Warning, config.Rules![1].Severity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_rule_ids_are_rejected(string? id) =>
        Assert.ThrowsAny<ArgumentException>(() => Lint.Configure(c => c.Rule(id!, JsonSerializer.SerializeToElement(new { }))));

    [Fact]
    public void Duplicate_configured_ids_are_rejected_but_case_is_significant()
    {
        var options = JsonSerializer.SerializeToElement(new { });
        var config = Lint.Configure(c => c.Rule("R", options).Rule("r", options));
        Assert.Equal(2, config.Rules!.Count);
        Assert.Throws<ArgumentException>(() => Lint.Configure(c => c.Rule("r", options).Rule("r", options)));
    }
}
