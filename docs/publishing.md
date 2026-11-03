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

## GitHub Packages

The release workflow also uploads the same tool package to `https://nuget.pkg.github.com/adrian13579/index.json`. It authenticates with the job's automatic `GITHUB_TOKEN` and `packages: write` permission, following [GitHub's workflow publishing guide](https://docs.github.com/en/packages/managing-github-packages-using-github-actions-workflows/publishing-and-installing-a-package-with-github-actions).

The package's `RepositoryUrl` connects it to this repository so it appears under **Packages**. The workflow verifies that association and the published version, then records the package link and visibility in its summary. Credentials exist only in the runner's temporary configuration, which is removed after the upload.

To publish an existing release to GitHub Packages, run the **Release** workflow from `main` and provide its existing version tag. Both registries skip duplicate versions, and the existing GitHub release is preserved.

## Release

1. Update `<Version>` in `src/SharpLint.Cli/SharpLint.Cli.csproj` and add `docs/release-notes/v<VERSION>.md`. Keep README installation commands version-free, using `--prerelease` for pre-alpha releases.
2. Commit, then push `main` and the matching `v<VERSION>` tag.
3. The release workflow builds and checks the installed package on Linux and Windows, runs the full suite with real Roslyn on Linux, publishes to NuGet.org and GitHub Packages, and creates a GitHub release with the `.nupkg` attached. GitHub releases are marked as regular releases and **Latest** so they appear in the repository sidebar. Pre-alpha package versions retain their SemVer prerelease suffix and still require `--prerelease` for version-free .NET tool installation.

To retry publication, run the **Release** workflow manually with the existing tag. NuGet duplicate versions are skipped; an existing GitHub release is preserved.

Local package validation, after a Debug build:

```sh
dotnet pack src/SharpLint.Cli -c Release -o artifacts/packages
pwsh -NoProfile -File tests/scripts/Test-ToolPackage.ps1
```

The package check installs from an isolated local feed and checks metadata, the command name, version, help, diagnostic exit codes and source-fix feedback using the controlled LSP server. Packaged READMEs use standard Markdown images and links; raw HTML outside code examples fails validation because NuGet displays it as text.
