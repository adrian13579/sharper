using System.Text.Json;
using SharpLint.Analysis;

namespace SharpLint.Tests.Lsp;

public sealed class NormalizationAndSynchronizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-sync-" + Guid.NewGuid().ToString("N"));
    public NormalizationAndSynchronizationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Parses_codes_positions_sources_related_information_and_project_findings()
    {
        var path = Path.Combine(_root, "space é #.cs");
        var uri = DiagnosticNormalizer.ToUri(path);
        Assert.Equal(path, DiagnosticNormalizer.FromUri(uri));
        Assert.Null(DiagnosticNormalizer.FromUri("https://example.com/file.cs"));
        Assert.Null(DiagnosticNormalizer.FromUri(null));
        var items = JsonSerializer.SerializeToElement(new object[]
        {
            new { code = 123, severity = 2, message = "warning", source = "Analyzer", range = new { start = new { line = 0, character = 2 }, end = new { line = 3, character = 4 } },
                relatedInformation = new[] { new { message = "related", location = new { uri, range = new { start = new { line = 1, character = 0 }, end = new { line = 1, character = 2 } } } } },
                data = new { project = "A" }, tags = new[] { 1 }, codeDescription = new { href = "https://example.com/rule" } },
            new { code = "GLOBAL001", message = "Project error", severity = 1 },
        });
        var parsed = DiagnosticNormalizer.Parse(items, uri);
        Assert.Equal("123", parsed[0].Code);
        Assert.Equal("warning", parsed[0].Severity);
        Assert.Equal("Analyzer", parsed[0].Source);
        Assert.Equal(path, parsed[0].FilePath);
        Assert.Equal(0, parsed[0].StartLine);
        Assert.Equal(2, parsed[0].StartColumn);
        Assert.Equal(3, parsed[0].EndLine);
        Assert.Equal(4, parsed[0].EndColumn);
        Assert.Equal("A", parsed[0].Data!.Value.GetProperty("project").GetString());
        Assert.Equal("https://example.com/rule", parsed[0].HelpUrl);
        Assert.Equal(1, Assert.Single(parsed[0].Tags!));
        Assert.Equal(path, Assert.Single(parsed[0].RelatedInformation!).FilePath);
        var project = Assert.Single(DiagnosticNormalizer.Parse(JsonSerializer.SerializeToElement(new[] { new { code = "GLOBAL001", severity = 1, message = "Project diagnostic" } }), null));
        Assert.Null(project.FilePath);
        Assert.Null(project.StartLine);
        Assert.Null(project.Source);
    }

    [Theory]
    [InlineData(1, "error")]
    [InlineData(2, "warning")]
    [InlineData(3, "information")]
    [InlineData(4, "hint")]
    [InlineData(19, "19")]
    public void Preserves_severities(int severity, string expected)
        => Assert.Equal(expected, Assert.Single(DiagnosticNormalizer.Parse(JsonSerializer.SerializeToElement(new[] { new { message = "test", severity } }), null)).Severity);

    [Fact]
    public void Ordering_deduplicates_only_identical_findings()
    {
        var a = new SharpLintDiagnostic { Code = "A", Message = "Message", Severity = "error", FilePath = "/A.cs", StartLine = 0, StartColumn = 0 };
        var b = a with { FilePath = "/B.cs" };
        var secondProject = a with { Data = JsonSerializer.SerializeToElement(new { project = "B" }) };
        var source = a with { Source = "Different provider" };
        var ordered = DiagnosticNormalizer.Order([b, a, a, secondProject, source]);
        Assert.Equal(4, ordered.Count);
        Assert.Equal(b, ordered[^1]);
        Assert.Equal(ordered, DiagnosticNormalizer.Order([source, secondProject, a, b, b]));
        Assert.Empty(DiagnosticNormalizer.Parse(JsonSerializer.SerializeToElement(Array.Empty<object>()), null));
        Assert.Equal("unspecified", Assert.Single(DiagnosticNormalizer.Parse(JsonSerializer.SerializeToElement(new[] { new { message = "test" } }), null)).Severity);
    }

    [Theory]
    [InlineData("x\r\n😀", 1, 2)]
    [InlineData("x\ry\n", 2, 0)]
    [InlineData("", 0, 0)]
    public void Incremental_changes_replace_the_old_utf16_range_and_advance_versions(string text, int line, int column)
    {
        var state = new DocumentState(text);
        var change = JsonSerializer.SerializeToElement(state.Change("new", 2));
        Assert.Equal(2, state.Version);
        Assert.Equal("new", state.Text);
        Assert.Equal(line, change.GetProperty("range").GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(column, change.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        var full = JsonSerializer.SerializeToElement(state.Change("latest", 1));
        Assert.False(full.TryGetProperty("range", out _));
        Assert.Equal("latest", full.GetProperty("text").GetString());
        Assert.Equal(3, state.Version);
        Assert.Throws<NotSupportedException>(() => state.Change("ignored", 0));
        Assert.Equal(3, state.Version);
    }

    [Fact]
    public async Task Snapshot_reconciliation_detects_same_length_edits_new_deleted_and_project_files()
    {
        var file = Path.Combine(_root, "Sample.cs");
        File.WriteAllText(file, "class A {}");
        File.WriteAllText(Path.Combine(_root, "Test.csproj"), "project");
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(Path.Combine(_root, "obj", "Generated.cs"), "ignored");
        using var synchronization = new FileSynchronization(_root);
        var before = await synchronization.ReadAsync();
        Assert.Equal(2, before.Files.Count);
        File.WriteAllText(file, "class B {}");
        var edited = await synchronization.ReadAsync();
        Assert.False(before.Matches(edited));
        Assert.False(edited.RequiresReload(before));
        Assert.Equal("class B {}", edited.Files[file].Text);
        Assert.True(edited.Matches(await synchronization.ReadAsync()));
        File.WriteAllText(Path.Combine(_root, "New.cs"), "new");
        Assert.True((await synchronization.ReadAsync()).RequiresReload(edited));
        File.Delete(file);
        Assert.True((await synchronization.ReadAsync()).RequiresReload(edited));
        File.WriteAllText(Path.Combine(_root, "Test.csproj"), "changed project");
        Assert.True((await synchronization.ReadAsync()).RequiresReload(edited));
    }

    [Fact]
    public async Task Source_snapshot_decodes_bom_from_the_same_captured_bytes()
    {
        var file = Path.Combine(_root, "Unicode.cs");
        File.WriteAllText(file, "// 😀\r\nclass Example {}", System.Text.Encoding.Unicode);
        using var synchronization = new FileSynchronization(_root);
        var snapshot = await synchronization.ReadAsync();
        Assert.Equal("// 😀\r\nclass Example {}", snapshot.Files[file].Text);
        Assert.True(snapshot.Matches(await synchronization.ReadAsync()));
    }

    [Fact]
    public void Watcher_hints_coalesce_duplicates_renames_and_overflow()
    {
        using var synchronization = new FileSynchronization(_root);
        var path = Path.Combine(_root, "Old.cs");
        synchronization.Record(path, 2);
        synchronization.Record(path, 2);
        synchronization.Record(path, 3);
        synchronization.Record(Path.Combine(_root, "New.cs"), 1);
        synchronization.Record(Path.Combine(_root, "obj", "ignored.cs"), 2);
        synchronization.MarkOverflow();
        var events = synchronization.Drain();
        Assert.True(events.Overflowed);
        Assert.Equal(2, events.Events.Count);
        Assert.Equal(3, events.Events[path]);
        Assert.Equal(5, synchronization.Epoch);
        Assert.Empty(synchronization.Drain().Events);
        Assert.False(synchronization.Drain().Overflowed);
    }
}
