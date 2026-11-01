using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SharpLint.Daemon;

public sealed record DaemonIdentity(string Root, string PipeName, string StateDirectory)
{
    public const int ProtocolVersion = 1;

    // Loaded module IDs describe the running code, even if a build replaces DLL files after this process starts.
    // Assembly versions alone are insufficient because development builds often retain the same version.
    public static string BuildId { get; } = Hash(string.Join("|", new[]
    {
        typeof(DaemonIdentity).Assembly,
        typeof(Analysis.WorkspaceCoordinator).Assembly,
        typeof(Core.Analysis.Analyzer).Assembly,
        typeof(Configuration.ConfigLoader).Assembly,
    }.Select(assembly => assembly.FullName + ":" + assembly.ManifestModule.ModuleVersionId)));

    public static DaemonIdentity Create(string root)
    {
        root = CanonicalPath(root);
        var environment = string.Join("|", Environment.UserName,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture, Environment.Version,
            Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT"), Environment.GetEnvironmentVariable("NUGET_PACKAGES"),
            Environment.GetEnvironmentVariable("PATH"));
        var user = Hash(environment)[..24];
        var key = Hash(environment + "|" + (OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root))[..40];
        return new DaemonIdentity(root, "sharplint-" + key, Path.Combine(Path.GetTempPath(), "sharplint-" + user));
    }

    public string LockPath(string purpose) => Path.Combine(StateDirectory, PipeName + "-" + purpose + ".lock");

    public void EnsureStateDirectory()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(StateDirectory);
        else Directory.CreateDirectory(StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if ((File.GetAttributes(StateDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Daemon state directory must not be a symbolic link.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(StateDirectory)
            & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new IOException("Daemon state directory must be private to the current user.");
    }

    private static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        var current = Path.GetPathRoot(full)!;
        foreach (var component in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new DirectoryInfo(Path.Combine(current, component));
            current = next.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next.FullName;
        }
        current = Path.TrimEndingDirectorySeparator(current);
        return current;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
