# Changelog

Changes to Sharper's source and user-facing behavior. Check [npm](https://www.npmjs.com/package/@adrian13579/sharper) for published package versions.

## Unreleased

### Worktrees

- Agent checkouts use separate language-server sessions, working directories and workspace roots, including linked Git worktrees.
- Requests inside a nested checkout select its own workspace; parent checks exclude nested checkouts and their file changes.

### Documentation and website

- Theme-aware JSON syntax highlighting in documentation examples.

- Tool response reference with examples and field descriptions for feedback, status, navigation and errors.

- Searchable website documentation with sidebar navigation, section links and light/dark themes.
- A concise landing page focused on rapid rule feedback, with live GitHub stars and total npm downloads.
- Unified server-profile and agent configuration instructions.
- A custom architecture-enforcer example showing how a team-owned LSP service can return project-specific rule findings.
- Contribution guidance centered on issue and pull request descriptions.

### CLI

- Guided setup now provides three clear steps, defaults, numbered agent choices and input validation with retries.
- New profiles are reviewed before saving; Copilot setup offers CLI, VS Code or both destinations.
- Help includes option descriptions and quick-start commands, with consistent plain-output support.
- Cancelling guided setup exits gracefully with code `130`; declining registration keeps the profile ready for later use.

### Tool responses

- Removed repeated limitations text from feedback, status and navigation results.
- Combined checks return compact per-service summaries alongside one findings array; single-service checks return the top-level result directly.
- Empty error lists, unused status placeholders and absent optional server details are omitted.
- Completion, freshness, findings and actionable errors remain explicit.

### Distribution

- npm/npx is the public distribution channel, with Windows x64 and Linux x64 executables bundled in the package.
- Release automation assembles both platforms and publishes through npm trusted publishing.

### Fixes

- Explicit agent configuration paths and XDG overrides work independently of a home-directory environment variable.

- Workspace read events preserve analysis freshness; writes and watcher rescan events continue to invalidate affected checks.
- Integration fixtures normalize temporary paths consistently across platforms.

### Maintenance

- CI separates release packaging from behavioral tests, reuses those checks for releases, and skips runtime jobs for documentation-only changes.
- Website tests focus on navigation, search, themes, statistics and accessibility rather than retired page copy.

- Removed obsolete landing-page styles, legacy benchmark reports and stale test setup files.
- Reduced unused runtime dependencies and consolidated website theme handling.

## 0.3.0-prealpha.1

Source milestone; package availability is tracked in npm.

### Added

- Language-agnostic server profiles mapping extensions to LSP language IDs and commands.
- Combined feedback from multiple configured services for matching files.
- Warm workspace/server sessions, synchronized documents and scoped completion/freshness evidence.
- Guided configuration and registration for supported coding agents.

### Changed

- Explicit LSP profiles replace built-in language routing and language-specific provisioning.
- Agents select the workspace and affected path while Sharper routes requests to configured services.
- Feedback, status and supporting symbol navigation share the MCP interface.

### Configuration changes

Existing registrations should be updated to pass `--lsp-config /absolute/path/servers.json`. Per-call `backend` and `solution` selectors and legacy server flags have been removed. See [server and agent setup](custom-language-servers.md).
