using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SharpLint.Analysis;

namespace SharpLint.Cli.Mcp;

[McpServerToolType]
public sealed class DiagnosticTools(WorkspaceSessions sessions)
{
    [McpServerTool(Name = "sharplint_check", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get C# compiler and analyzer feedback for the repository the agent is working on. Pass workspace as its absolute directory, use an absolute path to discover its repository, or use the MCP client's workspace roots. Sessions stay warm per repository. An omitted path checks its C# documents. Results cover document pull diagnostics; inspect complete, freshness, errors and limitations. Positions are zero-based UTF-16.")]
    public async Task<DiagnosticResponse> CheckAsync(McpServer server,
        [Description("Optional C# file or directory, absolute or relative to the selected repository.")] string? path = null,
        [Description("Absolute repository directory. Supply this when switching repositories or when the client does not provide workspace roots.")] string? workspace = null,
        [Description("Optional .sln or .slnx path within the repository; useful when it contains multiple solutions.")] string? solution = null,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(sessions.Timeout);
        try
        {
            var response = await sessions.CheckAsync(path, workspace, solution, await RootsAsync(server, workspace, path, deadline.Token), deadline.Token);
            return !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested && response.Status == "cancelled"
                ? response with { Status = "timedOut", Errors = ["Diagnostic check timed out."] } : response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new McpException("Repository discovery timed out. Pass workspace as an absolute repository directory."); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { throw new McpException(ex.Message, ex); }
    }

    [McpServerTool(Name = "sharplint_status", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Initialize or inspect the warm Roslyn session for a repository. Pass its absolute workspace directory, or use the MCP client's workspace roots. Reports readiness, capabilities, process identity and scope limitations.")]
    public async Task<ServiceStatus> StatusAsync(McpServer server,
        [Description("Absolute repository directory; optional when the client supplies one workspace root.")] string? workspace = null,
        [Description("Optional .sln or .slnx path within the repository.")] string? solution = null,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(sessions.Timeout);
        try { return await sessions.StatusAsync(workspace, solution, await RootsAsync(server, workspace, null, deadline.Token), deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new McpException("Repository status request timed out."); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { throw new McpException(ex.Message, ex); }
    }

    private async Task<IReadOnlyList<string>> RootsAsync(McpServer server, string? workspace, string? path, CancellationToken cancellationToken)
    {
#pragma warning disable MCP9005 // Compatibility with clients advertising roots on pre-2026-07-28 MCP; new clients pass workspace explicitly.
        if (workspace is not null || path is not null && Path.IsPathFullyQualified(path) || server.ClientCapabilities?.Roots is null) return [];
        var result = await server.RequestRootsAsync(new(), cancellationToken);
#pragma warning restore MCP9005
        return result.Roots.Select(root => Uri.TryCreate(root.Uri, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null)
            .OfType<string>().Where(Directory.Exists).ToArray();
    }
}
