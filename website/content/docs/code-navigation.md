# Supporting code navigation

Navigation is a useful addition to [feedback and rule verification](feedback.md). The same MCP tools work for any mapped language whose server advertises the corresponding LSP capability.

```json
{
  "workspace": "/work/app",
  "path": "src/example.orbit",
  "line": 5,
  "character": 18
}
```

Pass these arguments to sharper_definition or sharper_implementation. sharper_references additionally accepts includeDeclaration (default false). Path must identify an existing configured source file. Lines and characters are zero-based UTF-16, including positions after non-BMP characters.

The first configured navigation-enabled server advertising the operation handles the request. Set navigation false on rule-only services. A failed or unavailable selected service is explicit. Unsupported operations return status unsupported, complete false and freshness uncertain.

Responses retain locations, locationCount, requestedPath, position, operation, completion/freshness, errors and limitations. LocationLink results preserve targetRange, selection range and optional originSelectionRange. External/generated URIs survive without invented local paths. Results are sorted deterministically and only identical complete records are deduplicated.

Navigation synchronizes the configured source set and reconciles disk before/after the request, sharing sessions and deadlines with checks. Changing inputs or capabilities, malformed locations and server failures prevent verification. A verified navigation response means the supported request completed against stable observed inputs; it does not prove exhaustive symbol search, rule compliance or cross-runtime links. No rename, edits, code actions or arbitrary commands are exposed.

See [configuration](custom-language-servers.md) and [service guarantees](lsp-service.md).
