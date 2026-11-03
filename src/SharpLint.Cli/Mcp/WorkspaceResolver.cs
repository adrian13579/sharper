namespace SharpLint.Cli.Mcp;

internal static class WorkspaceResolver
{
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string Resolve(string? workspace, string? path, string? fallback, IReadOnlyList<string> roots)
    {
        if (workspace is not null)
        {
            if (!Path.IsPathFullyQualified(workspace)) throw new ArgumentException("workspace must be an absolute repository directory.");
            return DirectoryPath(workspace);
        }
        if (path is not null && Path.IsPathFullyQualified(path))
        {
            if (fallback is not null && Contains(fallback, path)) return DirectoryPath(fallback);
            var known = roots.Select(Discover).Distinct(PathComparer).ToArray();
            var containing = known.Where(root => Contains(root, path)).OrderByDescending(root => root.Length).FirstOrDefault();
            return containing ?? Discover(path);
        }
        var candidates = roots.Select(Discover).Distinct(PathComparer).ToArray();
        if (candidates.Length == 1) return candidates[0];
        if (candidates.Length == 0 && fallback is not null) return DirectoryPath(fallback);
        throw new ArgumentException(candidates.Length > 1
            ? "The agent has multiple repositories open. Pass workspace as the absolute repository directory, or pass an absolute path."
            : "Pass workspace as the absolute repository directory, or pass an absolute file/directory path. The MCP client has not supplied a repository root.");
    }

    internal static string Discover(string path)
    {
        var absolute = Path.GetFullPath(path);
        var start = Directory.Exists(absolute) ? absolute : File.Exists(absolute) ? Path.GetDirectoryName(absolute)! : throw new DirectoryNotFoundException($"Path not found: {absolute}");
        string? solution = null;
        string? project = null;
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return DirectoryPath(directory.FullName);
            if (solution is null && Directory.EnumerateFiles(directory.FullName).Any(f => Path.GetExtension(f) is ".sln" or ".slnx")) solution = directory.FullName;
            if (project is null && Directory.EnumerateFiles(directory.FullName, "*.csproj").Any()) project = directory.FullName;
        }
        return DirectoryPath(solution ?? project ?? start);
    }

    internal static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    internal static string DirectoryPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Workspace directory not found: {full}");
        return full;
    }
}
