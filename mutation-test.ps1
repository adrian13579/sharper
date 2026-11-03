# Measure the diagnostic service; CLI and real-server subprocess tests run separately.
# Additional arguments are forwarded to dotnet stryker.
$ErrorActionPreference = 'Stop'

Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build SharpLint.slnx -m:1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Set-Location -LiteralPath (Join-Path $PSScriptRoot 'tests/SharpLint.Tests')
    dotnet stryker --output (Join-Path $PSScriptRoot 'StrykerOutput/Analysis') @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

exit 0
