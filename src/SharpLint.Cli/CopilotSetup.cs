using System.Text.Json;
using System.Text.Json.Nodes;
using SharpLint.Cli.Mcp;

namespace SharpLint.Cli;

internal static class CopilotSetup
{
    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] != "copilot") throw new ArgumentException("Use sharplint setup copilot to configure GitHub Copilot CLI and VS Code.");
        var options = CliApplication.ParseOptions(args[1..], "--client", "--copilot-config", "--vscode-config", "--server");
        var client = options.GetValueOrDefault("--client", "both");
        if (client is not ("both" or "cli" or "vscode")) throw new ArgumentException("Client must be both, cli or vscode.");
        var targets = new List<(string Name, string Path, string Collection)>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (client is "both" or "cli")
        {
            var copilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
            targets.Add(("Copilot CLI", options.GetValueOrDefault("--copilot-config") ?? Path.Combine(string.IsNullOrWhiteSpace(copilotHome) ? Path.Combine(home, ".copilot") : copilotHome, "mcp-config.json"), "mcpServers"));
        }
        if (client is "both" or "vscode")
        {
            var basePath = OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support")
                : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config");
            targets.Add(("VS Code", options.GetValueOrDefault("--vscode-config") ?? Path.Combine(basePath, "Code", "User", "mcp.json"), "servers"));
        }
        var plans = targets.Select(target => Prepare(target.Name, target.Path, target.Collection, options.GetValueOrDefault("--server"))).ToArray();
        if (plans.Select(p => p.Path).Distinct(WorkspaceResolver.PathComparer).Count() != plans.Length)
            throw new ArgumentException("Copilot CLI and VS Code must use separate configuration files.");
        foreach (var plan in plans)
        {
            if (plan.Original != plan.Updated) Write(plan);
            Console.WriteLine($"Configured {plan.Name}: {plan.Path}");
        }
        Console.WriteLine("Sharplint is registered for all repositories. Restart the client's MCP connection, then ask the agent to use sharplint_check for its working repository.");
        return 0;
    }

    private static Plan Prepare(string name, string path, string collection, string? server)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path) && new FileInfo(path).ResolveLinkTarget(true) is { } target) path = target.FullName;
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        var text = original ?? "{}" + Environment.NewLine;
        var root = JsonNode.Parse(text, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new ArgumentException($"Configuration must be a JSON object: {path}");
        if (root.ContainsKey(collection) && root[collection] is not JsonObject) throw new ArgumentException($"{collection} must be an object: {path}");
        var servers = root[collection] as JsonObject;
        if (servers?["sharplint"] is not null and not JsonObject) throw new ArgumentException($"The sharplint entry must be an object: {path}");
        var entry = (servers?["sharplint"]?.DeepClone() as JsonObject) ?? new JsonObject();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running Sharplint executable.");
        var launch = new JsonArray();
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) launch.Add(typeof(CliApplication).Assembly.Location);
        launch.Add("mcp");
        if (server is not null)
        {
            launch.Add("--server");
            launch.Add(Path.IsPathRooted(server) || server.Contains(Path.DirectorySeparatorChar) || server.Contains(Path.AltDirectorySeparatorChar) ? Path.GetFullPath(server) : server);
        }
        entry["type"] = "stdio";
        entry["command"] = executable;
        entry["args"] = launch;
        if (collection == "mcpServers" && entry["tools"] is null) entry["tools"] = new JsonArray("*");
        var updated = JsonNode.DeepEquals(servers?["sharplint"], entry) ? text : JsonConfigEdit.SetServer(text, collection, entry, servers?.Count ?? 0);
        return new(name, path, original, updated);
    }

    private static void Write(Plan plan)
    {
        if ((File.Exists(plan.Path) ? File.ReadAllText(plan.Path) : null) != plan.Original)
            throw new IOException($"Configuration changed during setup; retry: {plan.Path}");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Path)!);
        var temporary = plan.Path + ".sharplint-" + Guid.NewGuid().ToString("N");
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, fileOptions))
            using (var writer = new StreamWriter(stream)) writer.Write(plan.Updated);
            if (!OperatingSystem.IsWindows() && File.Exists(plan.Path)) File.SetUnixFileMode(temporary, File.GetUnixFileMode(plan.Path));
            File.Move(temporary, plan.Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record Plan(string Name, string Path, string? Original, string Updated);
}
