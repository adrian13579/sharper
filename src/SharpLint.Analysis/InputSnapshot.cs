using System.Security.Cryptography;
using System.Text;
using SharpLint.Core.Analysis;

namespace SharpLint.Analysis;

// Source text and its content hash belong to the same captured byte sequence.
internal sealed record SourceInput(string Fingerprint, string Text);

internal sealed record InputSnapshot(string Fingerprint, string ProjectFingerprint,
    IReadOnlyDictionary<string, SourceInput> Sources)
{
    internal static bool IsRelevant(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".cs" or ".csproj" or ".fsproj" or ".vbproj" or ".sln" or ".slnx" or ".slnf" or
        ".props" or ".targets" or ".config" or ".json" or ".editorconfig" or ".globalconfig" or ".ruleset" or ".snk" or ".rsp"
        || Path.GetFileName(path) == ".editorconfig";

    internal static async Task<InputSnapshot> ReadAsync(string root, CancellationToken cancellationToken,
        bool includeSources = true,
        IEnumerable<string>? compilationInputs = null)
    {
        var paths = SourceFiles.EnumerateInputs(root).Where(IsRelevant).ToHashSet(StringComparer.Ordinal);
        var dependencies = (compilationInputs ?? []).ToHashSet(StringComparer.Ordinal);
        paths.UnionWith(dependencies);
        foreach (var directory in dependencies.Where(Directory.Exists))
            paths.UnionWith(SourceFiles.EnumerateInputs(directory).Where(IsRelevant));
        // Detect additions/removals in referenced projects outside the requested root as well.
        foreach (var project in dependencies.Where(p => Path.GetExtension(p).Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
            if (Directory.Exists(Path.GetDirectoryName(project)))
                paths.UnionWith(SourceFiles.EnumerateInputs(Path.GetDirectoryName(project)!).Where(IsRelevant));
        var generated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in paths.Where(p => Path.GetExtension(p).Equals(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var directory = Path.Combine(Path.GetDirectoryName(project)!, "obj");
            if (Directory.Exists(directory))
                generated.UnionWith(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(IsRelevant));
        }
        paths.UnionWith(generated);
        // SDK/package/build settings above the requested source directory also affect executable configuration.
        for (var parent = Directory.GetParent(root); parent is not null; parent = parent.Parent)
        {
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                         "global.json", "NuGet.config", "nuget.config" })
            {
                var path = Path.Combine(parent.FullName, name);
                if (File.Exists(path)) paths.Add(path);
            }
        }
        var sources = new Dictionary<string, SourceInput>(StringComparer.Ordinal);
        var inputs = new StringBuilder();
        var projects = new StringBuilder();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null;
            if (bytes is null)
            {
                var state = Directory.Exists(path) ? "directory" : "missing";
                inputs.Append(path.Length).Append(':').Append(path).Append(state);
                projects.Append(path.Length).Append(':').Append(path).Append(state);
                continue;
            }
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            inputs.Append(path.Length).Append(':').Append(path).Append(hash);
            if (generated.Contains(path)) projects.Append(path.Length).Append(':').Append(path).Append(hash);
            if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (!includeSources) continue;
                using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
                var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                sources.Add(path, new SourceInput(hash, text));
            }
            else projects.Append(path.Length).Append(':').Append(path).Append(hash);
        }
        return new InputSnapshot(Hash(inputs.ToString()), Hash(projects.ToString()), sources);
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
