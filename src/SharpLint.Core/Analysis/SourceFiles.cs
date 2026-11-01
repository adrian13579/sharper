namespace SharpLint.Core.Analysis;

/// <summary>The recursive, syntax-only source boundary shared by full and incremental hosts.</summary>
public static class SourceFiles
{
    public static IEnumerable<string> Enumerate(string directory) => EnumerateInputs(directory)
        .Where(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> EnumerateInputs(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(directory));
        var paths = new List<string>();
        while (pending.TryPop(out var current))
        {
            paths.AddRange(Directory.EnumerateFiles(current));
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(child);
                if (name is "bin" or "obj" or ".git" or ".sharplint") continue;
                // Do not follow links out of the repository or into directory cycles.
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
        }
        return paths.OrderBy(path => path, StringComparer.Ordinal);
    }
}
