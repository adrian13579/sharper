using System.Security.Cryptography;

namespace SharpLint.Analysis;

/// <summary>Disk reconciliation and a coalescing watcher hint queue, never a Roslyn workspace cache.</summary>
public sealed class FileSynchronization : IDisposable
{
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _events = new(PathComparer);
    private long _epoch;
    private bool _overflowed;
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public FileSynchronization(string root)
    {
        _root = Path.GetFullPath(root);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Record(e.FullPath, 1);
        _watcher.Changed += (_, e) => Record(e.FullPath, 2);
        _watcher.Deleted += (_, e) => Record(e.FullPath, 3);
        _watcher.Renamed += (_, e) => { Record(e.OldFullPath, 3); Record(e.FullPath, 1); };
        _watcher.Error += (_, _) => MarkOverflow();
        _watcher.EnableRaisingEvents = true;
    }

    public long Epoch => Interlocked.Read(ref _epoch);

    public void Record(string path, int type)
    {
        path = Path.GetFullPath(path);
        if (!IsRelevant(_root, path)) return;
        lock (_gate)
        {
            // Last event wins; snapshot comparison below resolves create/delete/rename sequences.
            _events[path] = type;
            Interlocked.Increment(ref _epoch);
        }
    }

    public void MarkOverflow()
    {
        lock (_gate) { _overflowed = true; Interlocked.Increment(ref _epoch); }
    }

    public (IReadOnlyDictionary<string, int> Events, bool Overflowed) Drain()
    {
        lock (_gate)
        {
            var result = (new Dictionary<string, int>(_events, PathComparer), _overflowed);
            _events.Clear();
            _overflowed = false;
            return result;
        }
    }

    public async Task<DiskSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var files = new Dictionary<string, DiskFile>(PathComparer);
        // Ignore reparse points rather than following a symlink outside the synchronization boundary.
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0 };
        var directories = new Stack<string>();
        directories.Push(_root);
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
            {
                if (Path.GetFileName(entry) == ".git") continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Cannot establish freshness across symbolic links: {entry}. Use a workspace without linked inputs.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                    continue;
                }
                if (!IsRelevant(_root, entry)) continue;
                var bytes = await File.ReadAllBytesAsync(entry, cancellationToken).ConfigureAwait(false);
                string? text = null;
                if (IsSource(entry))
                {
                    using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
                    text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                }
                // Text and fingerprint come from the same captured bytes, even when disk is changing.
                var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                files.Add(entry, new DiskFile(fingerprint, text));
            }
        }
        return new DiskSnapshot(files);
    }

    public static bool IsSource(string path) => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsRelevant(string root, string path)
    {
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        if (parts.Any(p => p == ".git")) return false;
        if (!parts.Any(p => p is "bin" or "obj")) return true;
        // Generated sources belong to the server; restored assets, analyzer binaries and imports affect reloads.
        return Path.GetFileName(path) == "project.assets.json" || Path.GetExtension(path) is ".dll" or ".props" or ".targets" or ".editorconfig";
    }

    public void Dispose() => _watcher.Dispose();
}

public sealed record DiskFile(string Fingerprint, string? Text);

public sealed record DiskSnapshot(IReadOnlyDictionary<string, DiskFile> Files)
{
    public bool Matches(DiskSnapshot other) => Files.Count == other.Files.Count
        && Files.All(pair => other.Files.TryGetValue(pair.Key, out var file) && pair.Value.Fingerprint == file.Fingerprint);

    public bool RequiresReload(DiskSnapshot previous) => !Files.Keys.ToHashSet(FileSynchronization.PathComparer).SetEquals(previous.Files.Keys)
        || Files.Any(pair => !FileSynchronization.IsSource(pair.Key)
            && previous.Files.TryGetValue(pair.Key, out var file) && pair.Value.Fingerprint != file.Fingerprint);
}
