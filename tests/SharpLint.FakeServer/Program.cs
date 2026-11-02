using System.Text;
using System.Text.Json;
using SharpLint.Analysis.Lsp;

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var documents = new Dictionary<string, (string Text, int Version)>();
var mode = File.Exists("mode.txt") ? File.ReadAllText("mode.txt").Trim() : "normal";
var pulls = 0;
async Task Send(object value) => await LspFraming.WriteAsync(output, JsonSerializer.SerializeToUtf8Bytes(value));
while (await LspFraming.ReadAsync(input) is { } bytes)
{
    using var json = JsonDocument.Parse(bytes);
    var message = json.RootElement;
    if (!message.TryGetProperty("method", out var method)) continue;
    var name = method.GetString();
    var parameters = message.TryGetProperty("params", out var p) ? p : default;
    var id = message.TryGetProperty("id", out var i) ? i.Clone() : default;
    switch (name)
    {
        case "initialize":
            if (mode == "crash-start") return;
            if (mode == "hang-start") { await Task.Delay(TimeSpan.FromMinutes(5)); return; }
            await Send(new { jsonrpc = "2.0", id, result = new { capabilities = new { textDocumentSync = new { openClose = true, change = mode == "full-sync" ? 1 : 2 } }, serverInfo = new { name = "SharpLint controlled test server" } } });
            break;
        case "initialized":
            if (mode != "push-only")
                await Send(new { jsonrpc = "2.0", id = "register", method = "client/registerCapability", @params = new { registrations = new[] { new { id = "diagnostics", method = "textDocument/diagnostic", registerOptions = new { identifier = "test", workspaceDiagnostics = false } } } } });
            await Send(new { jsonrpc = "2.0", method = "workspace/projectInitializationComplete" });
            if (mode == "load-error") await Send(new { jsonrpc = "2.0", method = "window/logMessage", @params = new { type = 1, message = "Missing project dependencies" } });
            if (mode == "load-error-request") await Send(new { jsonrpc = "2.0", id = "error-toast", method = "window/showMessageRequest", @params = new { type = 1, message = "Project loading failed" } });
            break;
        case "textDocument/didOpen":
            var opened = parameters.GetProperty("textDocument");
            documents[opened.GetProperty("uri").GetString()!] = (opened.GetProperty("text").GetString()!, opened.GetProperty("version").GetInt32());
            if (mode == "stale-push")
                await Send(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri = opened.GetProperty("uri").GetString(), version = 0,
                    diagnostics = new[] { new { code = "STALE", severity = 1, message = "Old push" } } } });
            break;
        case "textDocument/didChange":
            var changed = parameters.GetProperty("textDocument");
            var uri = changed.GetProperty("uri").GetString()!;
            if (changed.GetProperty("version").GetInt32() != documents[uri].Version + 1) return;
            documents[uri] = (parameters.GetProperty("contentChanges")[0].GetProperty("text").GetString()!, changed.GetProperty("version").GetInt32());
            break;
        case "textDocument/didClose": documents.Remove(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!); break;
        case "textDocument/diagnostic":
            pulls++;
            if (mode == "slow-check") await Task.Delay(300);
            if (mode == "refresh-always" || mode == "refresh-once" && pulls == 1)
                await Send(new { jsonrpc = "2.0", id = "refresh-" + pulls, method = "workspace/diagnostic/refresh" });
            if (mode == "unregister-diagnostics")
                await Send(new { jsonrpc = "2.0", id = "unregister", method = "client/unregisterCapability", @params = new { unregisterations = new[] { new { id = "diagnostics", method = "textDocument/diagnostic" } } } });
            if (mode == "crash-check") return;
            if (mode == "hang-check") { await Task.Delay(TimeSpan.FromMinutes(5)); return; }
            if (mode == "malformed") { await LspFraming.WriteAsync(output, Encoding.UTF8.GetBytes("not json")); break; }
            if (mode == "mutate") File.AppendAllText("Sample.cs", "\n// changed during analysis");
            if (mode == "delete") File.Delete("Sample.cs");
            if (mode == "project-mutate") File.AppendAllText("Test.csproj", "\n");
            var document = documents[parameters.GetProperty("textDocument").GetProperty("uri").GetString()!];
            var resultId = document.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (mode == "unchanged-without-cache" || parameters.TryGetProperty("previousResultId", out var previous) && previous.ValueKind == JsonValueKind.String && previous.GetString() == resultId)
                await Send(new { jsonrpc = "2.0", id, result = new { kind = "unchanged", resultId } });
            else
            {
                object[] items = document.Text.Contains("missing", StringComparison.Ordinal) ? [new { code = "CS0103", severity = 1, message = "Missing identifier", range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 7 } } }] : [];
                await Send(new { jsonrpc = "2.0", id, result = new { kind = "full", resultId = mode == "unknown-document" ? null : resultId, items } });
            }
            break;
        case "shutdown": await Send(new { jsonrpc = "2.0", id, result = (object?)null }); break;
        case "exit": return;
    }
}
