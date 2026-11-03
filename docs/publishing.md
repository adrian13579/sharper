# Publishing Sharplint

The CLI project defines the package version and produces the `sharplint` .NET tool. `sharplint --version` reads that assembly's version. Pre-alpha versions use `0.2.0-prealpha.N`.

## NuGet Trusted Publishing

Follow the [NuGet Trusted Publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) to create a policy with:

| Setting | Value |
| --- | --- |
| Repository owner | `adrian13579` |
| Repository | `sharplint` |
| Workflow file | `release.yml` |
| Environment | Leave blank |
| Package pattern | `sharplint` |
| Scope | Publish new packages and new versions |

Set the GitHub repository variable `NUGET_USER` to the NuGet.org profile username (`xtales`). The publish job uses `NuGet/login@v1` and `id-token: write` to obtain a temporary publishing key. Authentication happens after package validation.

## Release

1. Update `<Version>` in `src/SharpLint.Cli/SharpLint.Cli.csproj`, the README's installation commands, and `docs/release-notes/v<VERSION>.md`.
2. Commit, then push `main` and the matching `v<VERSION>` tag.
3. The release workflow builds and checks the installed package on Linux and Windows, runs the full suite with real Roslyn on Linux, publishes to NuGet, and creates a GitHub release with the `.nupkg` attached. Prerelease versions are marked as GitHub prereleases.

To retry publication, run the **Release** workflow manually with the existing tag. NuGet duplicate versions are skipped; an existing GitHub release is preserved.

Local package validation, after a Debug build:

```sh
dotnet pack src/SharpLint.Cli -c Release -o artifacts/packages
pwsh -NoProfile -File tests/scripts/Test-ToolPackage.ps1
```

The package check installs from an isolated local feed and checks metadata, the command name, version, help, diagnostic exit codes and source-fix feedback using the controlled LSP server.
