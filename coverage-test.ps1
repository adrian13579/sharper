# Run the full suite and report in-process diagnostic service coverage.
# Additional arguments are forwarded to dotnet test.
$ErrorActionPreference = 'Stop'

Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $coverageRunDir = Join-Path $PSScriptRoot ('TestResults/coverage/run-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $coverageRunDir -Force | Out-Null

    $testArguments = @(
        'test', 'SharpLint.slnx', '-m:1', '--collect:XPlat Code Coverage',
        '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'),
        '--results-directory', $coverageRunDir
    ) + $args
    dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $reportDir = Join-Path $PSScriptRoot 'CoverageReport'
    dotnet tool run reportgenerator "-reports:$coverageRunDir/**/coverage.cobertura.xml" "-targetdir:$reportDir" '-reporttypes:Html;TextSummary;JsonSummary;Cobertura;Badges'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Get-Content -LiteralPath (Join-Path $reportDir 'Summary.txt')
    Write-Output "HTML report: $(Join-Path $reportDir 'index.html')"
}
finally {
    Pop-Location
}

exit 0
