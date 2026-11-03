# Build the shared diagnostic service, CLI/MCP host and tests.
# Additional arguments are forwarded to dotnet build.
$ErrorActionPreference = 'Stop'

Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet build SharpLint.slnx -m:1 @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

exit 0
