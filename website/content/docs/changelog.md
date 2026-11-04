# Changelog

Changes to Sharper's source and user-facing behavior. Check [npm](https://www.npmjs.com/package/@adrian13579/sharper) for published package versions.

## Unreleased

### Documentation and website

- Searchable website documentation with sidebar navigation, section links and light/dark themes.
- A concise landing page focused on rapid rule feedback, with live GitHub stars and monthly npm downloads.
- Unified server-profile and agent configuration instructions.
- A custom architecture-enforcer example showing how a team-owned LSP service can return project-specific rule findings.
- Contribution guidance centered on issue and pull request descriptions.

### Distribution

- npm/npx is the public distribution channel, with Windows x64 and Linux x64 executables bundled in the package.
- Release automation assembles both platforms and publishes through npm trusted publishing.

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
