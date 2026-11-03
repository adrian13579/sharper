# Exercise the wrappers with a fake dotnet command; no .NET tools or Stryker run.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharplint scripts ' + [Guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $testRoot 'repo with spaces'
$wrapperTestState = [PSCustomObject]@{ Calls = @(); FailAt = 0; Assertions = 0 }

function Assert-True([bool] $Condition, [string] $Message) {
    $wrapperTestState.Assertions++
    if (-not $Condition) { throw $Message }
}

function dotnet {
    $wrapperTestState.Calls += [PSCustomObject]@{
        Directory = (Get-Location).Path
        Arguments = @($args)
    }
    Set-Variable -Name LASTEXITCODE -Value 0 -Scope 1
    if ($wrapperTestState.Calls.Count -eq $wrapperTestState.FailAt) {
        Set-Variable -Name LASTEXITCODE -Value 37 -Scope 1
        return
    }

    if ($args[0] -eq 'tool' -and $args[1] -eq 'run') {
        $reportDir = Join-Path $fixtureRoot 'CoverageReport'
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $reportDir 'Summary.txt') -Value 'Fake coverage summary'
    }
}

New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'tests/SharpLint.Tests') -Force | Out-Null
Push-Location -LiteralPath $testRoot
try {
    $cases = @(
        @{ Name = 'build'; Steps = 1 },
        @{ Name = 'coverage-test'; Steps = 3 },
        @{ Name = 'mutation-test'; Steps = 3 }
    )
    foreach ($shellScript in Get-ChildItem -LiteralPath $repoRoot -Filter '*.sh' -File) {
        Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot ($shellScript.BaseName + '.ps1'))) "Missing PowerShell equivalent for $($shellScript.Name)"
    }

    foreach ($case in $cases) {
        $scriptPath = Join-Path $fixtureRoot ($case.Name + '.ps1')
        Copy-Item -LiteralPath (Join-Path $repoRoot ($case.Name + '.ps1')) -Destination $scriptPath
        $wrapperTestState.Calls = @()
        $wrapperTestState.FailAt = 0
        $extraArguments = @('--example-option', 'value with spaces')
        $output = @(& $scriptPath @extraArguments)
        Assert-True ($LASTEXITCODE -eq 0) "$($case.Name): success exit code"
        Assert-True ($wrapperTestState.Calls.Count -eq $case.Steps) "$($case.Name): command count"
        Assert-True ((Get-Location).Path -eq $testRoot) "$($case.Name): caller location restored"

        $targetCall = if ($case.Name -eq 'coverage-test') { $wrapperTestState.Calls[1] } else { $wrapperTestState.Calls[-1] }
        Assert-True ($targetCall.Arguments[-2] -eq '--example-option' -and $targetCall.Arguments[-1] -eq 'value with spaces') "$($case.Name): arguments forwarded intact"

        foreach ($call in $wrapperTestState.Calls) {
            $expectedDirectory = if ($call.Arguments[0] -eq 'stryker') { Join-Path $fixtureRoot 'tests/SharpLint.Tests' } else { $fixtureRoot }
            Assert-True ($call.Directory -eq $expectedDirectory) "$($case.Name): repository-relative working directory"
        }

        if ($case.Name -eq 'build') {
            Assert-True ($targetCall.Arguments[0] -eq 'build' -and $targetCall.Arguments -contains 'SharpLint.slnx') 'Build targets the solution'
        }
        elseif ($case.Name -eq 'mutation-test') {
            Assert-True ($wrapperTestState.Calls[0].Arguments[0] -eq 'tool' -and $wrapperTestState.Calls[0].Arguments[1] -eq 'restore') 'Mutation restores local tools'
            Assert-True ($wrapperTestState.Calls[1].Arguments[0] -eq 'build') 'Mutation builds before measuring'
            Assert-True ($targetCall.Arguments[0] -eq 'stryker' -and $targetCall.Arguments -contains (Join-Path $fixtureRoot 'StrykerOutput/Analysis')) 'Mutation output path'
        }
        else {
            Assert-True ($wrapperTestState.Calls[0].Arguments[0] -eq 'tool' -and $wrapperTestState.Calls[0].Arguments[1] -eq 'restore') 'Coverage restores local tools'
            Assert-True ($targetCall.Arguments -contains '--collect:XPlat Code Coverage') 'Coverage collector name remains one argument'
            Assert-True ($targetCall.Arguments -contains (Join-Path $fixtureRoot 'coverage.runsettings')) 'Coverage uses repository settings'
            $resultsIndex = [Array]::IndexOf($targetCall.Arguments, '--results-directory') + 1
            $firstRunDir = $targetCall.Arguments[$resultsIndex]
            Assert-True (Test-Path -LiteralPath $firstRunDir -PathType Container) 'Coverage creates a run directory'
            $reportArguments = $wrapperTestState.Calls[2].Arguments
            Assert-True ($reportArguments -contains "-reports:$firstRunDir/**/coverage.cobertura.xml") 'Coverage reports use only the current run'
            Assert-True ($reportArguments -contains '-reporttypes:Html;TextSummary;JsonSummary;Cobertura;Badges') 'Report types remain one argument'
            Assert-True ($output -contains 'Fake coverage summary') 'Coverage prints the summary'
            Assert-True ($output -contains "HTML report: $(Join-Path $fixtureRoot 'CoverageReport/index.html')") 'Coverage prints the HTML report location'

            $wrapperTestState.Calls = @()
            & $scriptPath | Out-Null
            Assert-True ($wrapperTestState.Calls[1].Arguments[$resultsIndex] -ne $firstRunDir) 'Repeated coverage runs use separate directories'
        }

        for ($step = 1; $step -le $case.Steps; $step++) {
            $wrapperTestState.Calls = @()
            $wrapperTestState.FailAt = $step
            & $scriptPath | Out-Null
            Assert-True ($LASTEXITCODE -eq 37) "$($case.Name): propagate exit code from step $step"
            Assert-True ($wrapperTestState.Calls.Count -eq $step) "$($case.Name): stop after failed step $step"
            Assert-True ((Get-Location).Path -eq $testRoot) "$($case.Name): restore location after failed step $step"
        }
    }

    Write-Output "$($wrapperTestState.Assertions) PowerShell script assertions passed. All dotnet commands were faked."
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

exit 0
