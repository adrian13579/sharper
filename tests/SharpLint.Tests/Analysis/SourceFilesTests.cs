using SharpLint.Core.Analysis;

namespace SharpLint.Tests.Analysis;

public sealed class SourceFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sharplint-inputs-" + Guid.NewGuid().ToString("N"));
    public SourceFilesTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Sources_are_sorted_case_insensitive_by_extension_and_exclude_build_and_git_directories()
    {
        foreach (var directory in new[] { "bin", "obj", ".git", ".sharplint", "nested/bin", "nested/obj", "nested/.git", "nested" })
        {
            var path = Path.Combine(_root, directory);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "Bad.cs"), "class bad {}");
        }
        File.WriteAllText(Path.Combine(_root, "Z.CS"), "class Good {}");
        File.WriteAllText(Path.Combine(_root, "A.cs"), "class Good {}");
        File.WriteAllText(Path.Combine(_root, "ignored.txt"), "class bad {}");
        Assert.Equal(new[] { Path.Combine(_root, "A.cs"), Path.Combine(_root, "Z.CS"), Path.Combine(_root, "nested", "Bad.cs") },
            SourceFiles.Enumerate(_root));
        Assert.Contains(Path.Combine(_root, "ignored.txt"), SourceFiles.EnumerateInputs(_root));
    }

    [Fact]
    public void Directory_symlinks_do_not_escape_the_source_root_or_create_cycles()
    {
        if (OperatingSystem.IsWindows()) return; // Symlink privileges are not available on all Windows test hosts.
        var child = Path.Combine(_root, "child");
        Directory.CreateDirectory(child);
        var path = Path.Combine(child, "A.cs");
        File.WriteAllText(path, "class Good {}");
        Directory.CreateSymbolicLink(Path.Combine(child, "cycle"), _root);
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), child);
        Assert.Equal(new[] { path }, SourceFiles.Enumerate(_root));
    }
}
