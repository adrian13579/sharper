using System.ComponentModel;
using ModelContextProtocol.Server;
using SharpLint.Analysis;

namespace SharpLint.Cli.Mcp;

[McpServerToolType]
public sealed class DiagnosticTools(DiagnosticService service)
{
    [McpServerTool(Name = "sharplint_check", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reconcile disk and check C# document diagnostics using a persistent Roslyn session. Default: all .cs documents under the workspace root, excluding bin/obj. This is not full solution/build verification. Positions are zero-based UTF-16; paths are absolute. Inspect complete, freshness, errors and limitations.")]
    public Task<DiagnosticResponse> CheckAsync(
        [Description("Optional C# file or directory, absolute or relative to the workspace root.")] string? path = null,
        CancellationToken cancellationToken = default) => service.CheckAsync(path, cancellationToken);

    [McpServerTool(Name = "sharplint_status", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Initialize or inspect the persistent Roslyn session, workspace readiness, diagnostic capabilities, last error and scope limitations. Does not run a diagnostic check.")]
    public Task<ServiceStatus> StatusAsync(CancellationToken cancellationToken) => service.StatusAsync(cancellationToken);
}
