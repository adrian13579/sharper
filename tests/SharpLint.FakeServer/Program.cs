using System.Text;
using System.Text.Json;
using SharpLint.Analysis.Lsp;

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var documents = new Dictionary<string, (string Text, int Version)>();
var mode = File.Exists("mode.txt") ? File.ReadAllText("mode.txt").Trim() : "normal";
var pulls = 0;
var expectedResponses = new HashSet<string>(StringComparer.Ordinal);
var watchedChanges = new List<JsonElement>();
async Task Send(object value) => await LspFraming.WriteAsync(output, JsonSerializer.SerializeToUtf8Bytes(value));
static void Require(bool condition, string description)
{
    if (!condition) throw new InvalidDataException("Client protocol violation: " + description);
}
while (await LspFraming.ReadAsync(input) is { } bytes)
{
    using var json = JsonDocument.Parse(bytes);
    var message = json.RootElement;
    if (!message.TryGetProperty("method", out var method))
    {
        if (mode != "handshake") continue;
        var responseId = message.GetProperty("id").GetString()!;
        if (!expectedResponses.Remove(responseId)) continue;
        var value = message.TryGetProperty("result", out var result) ? result : default;
        switch (responseId)
        {
            case "configuration":
                Require(value.GetArrayLength() == 4 && value[0].ValueKind == JsonValueKind.False
                    && value[1].GetString() == "openFiles" && value[2].ValueKind == JsonValueKind.Null
                    && value[3].ValueKind == JsonValueKind.Null, "configuration defaults and disabled restore");
                break;
            case "folders":
                Require(value.GetArrayLength() == 1 && value[0].GetProperty("uri").GetString() == new Uri(Directory.GetCurrentDirectory()).AbsoluteUri
                    && value[0].GetProperty("name").GetString() == Path.GetFileName(Directory.GetCurrentDirectory()), "workspace folders");
                break;
            case "edit": Require(!value.GetProperty("applied").GetBoolean() && value.GetProperty("failureReason").GetString()!.Contains("read-only", StringComparison.Ordinal), "read-only edit rejection"); break;
            case "progress": Require(value.ValueKind == JsonValueKind.Null, "explicit null progress acknowledgement"); break;
            case "unknown": Require(message.GetProperty("error").GetProperty("code").GetInt32() == -32601, "unknown request response"); break;
        }
        if (expectedResponses.Count == 0) await Send(new { jsonrpc = "2.0", method = "workspace/projectInitializationComplete" });
        continue;
    }
    var name = method.GetString();
    var parameters = message.TryGetProperty("params", out var p) ? p : default;
    var id = message.TryGetProperty("id", out var i) ? i.Clone() : default;
    switch (name)
    {
        case "initialize":
            if (mode == "crash-start") return;
            if (mode == "hang-start") { await Task.Delay(TimeSpan.FromMinutes(5)); return; }
            if (mode == "stderr-failure")
            {
                await Console.Error.WriteLineAsync(new string('x', 9000) + " unsupported-runtime detail");
                await Send(new { jsonrpc = "2.0", id, error = new { code = -32603, message = "Unsupported runtime" } });
                return;
            }
            object capabilities = mode switch
            {
                "static-object" => new { textDocumentSync = 1, diagnosticProvider = new { workspaceDiagnostics = true } },
                "static-boolean" => new { textDocumentSync = 2, diagnosticProvider = true },
                _ => new { textDocumentSync = new { openClose = mode != "no-open-close", change = mode == "no-sync" ? 0 : mode == "full-sync" ? 1 : 2 } },
            };
            if (mode == "no-server-info") await Send(new { jsonrpc = "2.0", id, result = new { capabilities } });
            else await Send(new { jsonrpc = "2.0", id, result = new { capabilities, serverInfo = new { name = "SharpLint controlled test server" } } });
            break;
        case "initialized":
            if (mode is not ("push-only" or "static-object" or "static-boolean"))
                await Send(new { jsonrpc = "2.0", id = "register", method = "client/registerCapability", @params = new { registrations = new[] { new { id = "diagnostics", method = "textDocument/diagnostic", registerOptions = new { identifier = "test", workspaceDiagnostics = false } } } } });
            if (mode == "watched-files")
                await Send(new { jsonrpc = "2.0", id = "watch", method = "client/registerCapability", @params = new { registrations = new[] { new { id = "watcher", method = "workspace/didChangeWatchedFiles", registerOptions = new { watchers = new[] { new { globPattern = "**/*" } } } } } } });
            if (mode == "handshake")
            {
                expectedResponses.UnionWith(["configuration", "folders", "edit", "progress", "unknown"]);
                await Send(new { jsonrpc = "2.0", id = "configuration", method = "workspace/configuration", @params = new { items = new[]
                {
                    new { section = (string?)"projects.dotnet_enable_automatic_restore" },
                    new { section = (string?)"csharp|background_analysis.dotnet_analyzer_diagnostics_scope" },
                    new { section = (string?)"unknown.setting" }, new { section = (string?)null },
                } } });
                await Send(new { jsonrpc = "2.0", id = "folders", method = "workspace/workspaceFolders" });
                await Send(new { jsonrpc = "2.0", id = "edit", method = "workspace/applyEdit", @params = new { edit = new { changes = new { } } } });
                await Send(new { jsonrpc = "2.0", id = "progress", method = "window/workDoneProgress/create", @params = new { token = "loading" } });
                await Send(new { jsonrpc = "2.0", method = "window/logMessage", @params = new { type = 2, message = "A warning is not a workspace failure" } });
                await Send(new { jsonrpc = "2.0", method = "custom/ignoredNotification" });
                await Send(new { jsonrpc = "2.0", id = "unknown", method = "custom/unknownRequest" });
            }
            else await Send(new { jsonrpc = "2.0", method = "workspace/projectInitializationComplete" });
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
            if (mode == "watched-files") Require(watchedChanges.Any(c => c.GetProperty("uri").GetString() == uri && c.GetProperty("type").GetInt32() == 2), "changed-file event before content synchronization");
            if (changed.GetProperty("version").GetInt32() != documents[uri].Version + 1) return;
            documents[uri] = (parameters.GetProperty("contentChanges")[0].GetProperty("text").GetString()!, changed.GetProperty("version").GetInt32());
            break;
        case "textDocument/didClose": documents.Remove(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!); break;
        case "workspace/didChangeWatchedFiles":
            watchedChanges.AddRange(parameters.GetProperty("changes").EnumerateArray().Select(c => c.Clone()));
            if (File.Exists("audit-path.txt"))
                File.AppendAllLines(File.ReadAllText("audit-path.txt"), parameters.GetProperty("changes").EnumerateArray().Select(c => c.GetRawText()));
            break;
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
                await Send(new { jsonrpc = "2.0", id, result = new { kind = "unchanged", resultId = mode == "mismatched-unchanged" ? "wrong" : resultId } });
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
