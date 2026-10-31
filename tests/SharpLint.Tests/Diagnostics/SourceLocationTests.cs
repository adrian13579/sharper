using System.Text.Json;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Tests.Diagnostics;

public sealed class SourceLocationTests
{
    [Theory]
    [InlineData("Sample.cs", 1, 1)]
    [InlineData("src/My Project/Sample.cs", 42, 17)]
    [InlineData("src/日本語/Sample.cs", 120, 80)]
    public void Location_preserves_file_path_and_one_based_coordinates(string path, int line, int column)
    {
        var location = new SourceLocation(path, line, column);

        Assert.Equal(path, location.FilePath);
        Assert.Equal(line, location.Line);
        Assert.Equal(column, location.Column);
    }

    [Fact]
    public void Diagnostic_serialization_preserves_source_location()
    {
        var diagnostic = new Diagnostic("naming.class.convention", Severity.Error,
            "Class name must use PascalCase.", new SourceLocation("src/My Project/Sample.cs", 42, 17));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(diagnostic, options);
        using var document = JsonDocument.Parse(json);
        var location = document.RootElement.GetProperty("location");
        Assert.Equal("src/My Project/Sample.cs", location.GetProperty("filePath").GetString());
        Assert.Equal(42, location.GetProperty("line").GetInt32());
        Assert.Equal(17, location.GetProperty("column").GetInt32());

        var restored = JsonSerializer.Deserialize<Diagnostic>(json, options)!;
        Assert.Equal(diagnostic.Location.FilePath, restored.Location.FilePath);
        Assert.Equal(diagnostic.Location.Line, restored.Location.Line);
        Assert.Equal(diagnostic.Location.Column, restored.Location.Column);
    }
}
