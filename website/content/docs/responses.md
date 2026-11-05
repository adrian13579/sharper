# Tool response format

Sharper returns structured JSON so an agent can read findings, decide what to fix and confirm whether a check is current. This guide describes the compact response format used by the MCP tools. Example paths, service names and timings are illustrative.

## MCP result wrapper

The JSON-RPC envelope contains `jsonrpc: "2.0"`, an `id` matching the request, and a `result` object. An MCP `tools/call` result has these fields:

| Field               | Meaning                                                                                                                                                                                   |
| ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `structuredContent` | The JSON payload for a returned analysis or status result. The examples below show this payload.                                                                                          |
| `content`           | An array with a text item: `type` is `"text"`, and `text` contains the same payload serialized as compact JSON. For an invocation error, it contains the error message instead.           |
| `isError`           | `false` when Sharper returns an analysis/status payload, including incomplete or failed analysis. `true` for an invocation error, such as a configuration or workspace-selection problem. |

Read the payload's `status`, `complete` and `freshness` together to assess analysis. Read its findings to assess rule compliance.

## Check results

`sharper_check` returns findings for the requested file or directory:

```json
{
  "status": "completed",
  "complete": true,
  "freshness": "verified",
  "scope": "file",
  "requestedPath": "/work/app/src/example.orbit",
  "backend": "project-rules",
  "diagnosticCount": 1,
  "diagnostics": [
    {
      "backend": "project-rules",
      "code": "RULE001",
      "severity": "error",
      "message": "Configured rule violation",
      "filePath": "/work/app/src/example.orbit",
      "startLine": 3,
      "startColumn": 8,
      "endLine": 3,
      "endColumn": 13,
      "source": "team-rules"
    }
  ],
  "durationMs": 18.2
}
```

| Field             | Meaning                                                                                                                                                                            |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `status`          | Outcome of the analysis request. See the status values below.                                                                                                                      |
| `complete`        | `true` when all selected services complete analysis and the reconciled inputs remain stable.                                                                                       |
| `freshness`       | `"verified"` for a current completed result, or `"uncertain"` when the request could not establish that evidence.                                                                  |
| `scope`           | `"file"` for a file check, or `"documents"` for mapped documents in a requested directory.                                                                                         |
| `requestedPath`   | Normalized absolute path of the checked file or directory.                                                                                                                         |
| `backend`         | Configured service ID for a single service, or `"mixed"` for a combined check. This is provenance; Sharper selects services automatically from the profile.                        |
| `diagnosticCount` | Number of findings in `diagnostics`.                                                                                                                                               |
| `diagnostics`     | Deterministically ordered findings. A completed clean check has an empty array and a count of zero.                                                                                |
| `errors`          | Optional array of actionable analysis-error messages. Omitted when empty. For combined checks, service errors include the relevant service ID.                                     |
| `durationMs`      | Elapsed engine-operation time in milliseconds, including initialization when needed. Client-root discovery, waiting for the engine and MCP transport are outside this measurement. |
| `backendResults`  | Present for combined checks: compact per-service outcomes, described below.                                                                                                        |

A completed check can contain error-severity findings, as in the example. `complete` and `freshness` describe analysis evidence; diagnostic severities describe the code.

### Status values

| Value         | Meaning                                                                                                                                                                                  |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `completed`   | The operation finished with current, complete analysis.                                                                                                                                  |
| `incomplete`  | Analysis returned partial or uncertain evidence, for example after an input change or an unavailable capability. A combined check uses this when any selected service fails to complete. |
| `failed`      | A single-service analysis failed, for example because a service exited or returned malformed data.                                                                                       |
| `cancelled`   | The client cancelled the request.                                                                                                                                                        |
| `timedOut`    | The request deadline expired.                                                                                                                                                            |

`completed` pairs with `complete: true` and `freshness: "verified"`. Other analysis outcomes use `complete: false` and `freshness: "uncertain"`. Returned findings can still help identify a correction while you address the reported error and check again.

### Findings and related information

Diagnostic positions are **zero-based UTF-16**. The end position is exclusive. For example, `startLine: 3` refers to the fourth line, and a character outside the basic multilingual plane takes two UTF-16 code units.

| Diagnostic field           | Meaning                                                                                                                                                          |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `backend`                  | ID of the configured service that produced this finding.                                                                                                         |
| `code`                     | Diagnostic code as a string. Numeric server codes are converted to strings; an omitted code becomes `""`.                                                        |
| `severity`                 | `"error"`, `"warning"`, `"information"` or `"hint"`. An omitted severity becomes `"unknown"`; an unrecognized numeric severity is preserved as a numeric string. |
| `message`                  | The service's diagnostic message.                                                                                                                                |
| `filePath`                 | Normalized absolute local path. URI-based related locations that cannot be mapped to a local path use `null`.                                                    |
| `startLine`, `startColumn` | Start of the finding's range, using zero-based line and UTF-16 column offsets.                                                                                   |
| `endLine`, `endColumn`     | Exclusive end of the range, using the same units.                                                                                                                |
| `source`                   | Optional source label supplied by the service.                                                                                                                   |
| `helpUrl`                  | Optional documentation link from the service's diagnostic code description.                                                                                      |
| `tags`                     | Optional numeric tags from the service. Standard tags include `1` for unnecessary code and `2` for deprecated code; additional values are preserved.             |
| `data`                     | Optional opaque JSON supplied by the service. Its value is preserved, including `null`, nested objects and arrays.                                               |
| `relatedInformation`       | Optional array of supporting locations and messages.                                                                                                             |

Each `relatedInformation` entry contains `message`, `filePath`, `startLine`, `startColumn`, `endLine` and `endColumn`. Its path and position conventions match the finding.

### Combined checks

A combined check keeps findings in the top-level `diagnostics` array and includes a compact `backendResults` summary. This excerpt illustrates the summary fields:

```json
{
  "backend": "mixed",
  "backendResults": [
    {
      "backend": "language-service",
      "status": "completed",
      "diagnosticCount": 0
    },
    { "backend": "project-rules", "status": "completed", "diagnosticCount": 1 }
  ]
}
```

Each entry contains the service ID (`backend`), its analysis outcome (`status`) and the number of findings it produced (`diagnosticCount`). Use the top-level `complete` and `freshness` for the overall result: an input change can invalidate the combined result even when individual services finished.

## Status results

`sharper_status` reports connection health and advertised diagnostic capabilities:

```json
{
  "backend": "language-service",
  "available": true,
  "workspaceState": "ready",
  "workspace": "/work/app",
  "diagnosticsAvailable": true,
  "sessionStarts": 1,
  "processId": 1234,
  "serverName": "Example language service",
  "workspaceDiagnosticCapability": false
}
```

| Field                           | Meaning                                                                                                                                                                                         |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `backend`                       | Configured service ID, or `"mixed"` when reporting several services.                                                                                                                            |
| `available`                     | Whether the service process is available. For a combined response, all selected processes must be available.                                                                                    |
| `workspaceState`                | `"ready"` or `"failed"`. A single session reports its process/readiness state; a combined response reflects aggregate process availability.                                                     |
| `workspace`                     | Normalized absolute workspace path.                                                                                                                                                             |
| `diagnosticsAvailable`          | Whether the service advertises the diagnostic interface needed for its configured mode. For combined status, every selected service must provide it.                                            |
| `lastError`                     | Latest recorded session error, or startup errors joined into one message for combined status. Omitted when absent.                                                                              |
| `sessionStarts`                 | Startup counter. A single session records the engine's startup count when it was created; combined status reports the engine's current total. A warm reused session retains its original value. |
| `processId`                     | Optional operating-system process ID, present for a single live session when available.                                                                                                         |
| `serverName`                    | Optional name supplied in the service's initialization response.                                                                                                                                |
| `workspaceDiagnosticCapability` | Whether the service advertises LSP workspace-diagnostic support. In combined status, `true` when any selected service advertises it. Sharper's checks use document diagnostics.                 |
| `sessions`                      | Present for combined status: individual service status objects using these fields, with the shared `workspace` kept at the top level.                                                           |

Use status to inspect connections and capabilities, and `sharper_check` to obtain current findings.

## Cancellation, timeouts and invocation errors

A cancelled or timed-out request returns a compact payload:

```json
{
  "status": "cancelled",
  "complete": false,
  "freshness": "uncertain",
  "errors": ["Request cancelled; no result is verified"]
}
```

A timeout uses `"status": "timedOut"` and the corresponding error message. These payloads contain the request outcome; finding arrays and scope details appear when an analysis result is available.

For an invocation problem such as missing service configuration or an unresolved workspace, the MCP result has `isError: true` and a text message in `content`. Read that message, correct the configuration or arguments and retry.

Malformed protocol requests use a JSON-RPC `error` object in place of `result`. It contains a numeric `code` and a `message`. Sharper uses `-32700` for invalid/oversized JSON messages, `-32600` for invalid requests, duplicate IDs or too many active requests, `-32601` for unsupported methods or missing initialization, and `-32602` for invalid tool names or argument shapes.

See [Overview](overview.md#read-the-result) for the editing workflow and [server configuration](custom-language-servers.md) for profile options.
