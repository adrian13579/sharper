using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SharpLint.Daemon;

public sealed record DaemonIdentity(string Root, string PipeName, string StateDirectory)
{
    public const int ProtocolVersion = 2;

    // Read managed module metadata without loading assemblies into the native client.
    // Capture once so a running host retains its identity when a build replaces its files.
    private static readonly Lazy<string> s_buildId = new(GetBuildId);
    public static string BuildId => s_buildId.Value;

    public static string HostPath => Path.Combine(HostDirectory, "SharpLint.Daemon.dll");

    private static string HostDirectory => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "daemon"))
        ? Path.Combine(AppContext.BaseDirectory, "daemon") : AppContext.BaseDirectory;

    private static string GetBuildId()
    {
        var modules = new[] { "Daemon", "Daemon.Client", "Analysis", "Core", "Configuration" };
        try
        {
            return Hash(string.Join("|", modules.Select(name =>
            {
                using var stream = File.OpenRead(Path.Combine(HostDirectory, $"SharpLint.{name}.dll"));
                using var reader = new PEReader(stream);
                var metadata = reader.GetMetadataReader();
                return name + ":" + metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            })));
        }
        catch (IOException ex)
        {
            throw new DaemonCommunicationException($"Cannot read the managed daemon bundle: {ex.Message}", ex);
        }
    }

    public static DaemonIdentity Create(string root)
    {
        root = CanonicalPath(root);
        var environment = string.Join("|", Environment.UserName,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture, ProtocolVersion,
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
