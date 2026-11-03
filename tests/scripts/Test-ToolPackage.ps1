param(
    [string] $PackageDirectory = (Join-Path $PSScriptRoot '../../artifacts/packages')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/SharpLint.Cli/SharpLint.Cli.csproj') -Raw)
$version = $project.Project.PropertyGroup.Version
$packagePath = Join-Path (Resolve-Path -LiteralPath $PackageDirectory).Path "sharplint.$version.nupkg"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharplint package ' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $reader = New-Object IO.StreamReader($archive.GetEntry('sharplint.nuspec').Open())
    try { $manifest = [xml]$reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $metadata = $manifest.package.metadata
    Assert-True ($metadata.id -eq 'sharplint') 'Package ID must be sharplint'
    Assert-True ($metadata.version -eq $version) 'Package version must match the project'
    Assert-True ($metadata.packageTypes.packageType.name -eq 'DotnetTool') 'Package must be a .NET tool'
    Assert-True ($metadata.license.InnerText -eq 'Apache-2.0') 'Package license must be Apache-2.0'
    Assert-True ($metadata.repository.url -eq 'https://github.com/adrian13579/sharplint') 'Package must link to its GitHub repository'
    foreach ($entry in @('README.md', 'LICENSE', 'tools/net10.0/any/sharplint.dll', 'tools/net10.0/any/SharpLint.Analysis.dll')) {
        Assert-True ($null -ne $archive.GetEntry($entry)) "Missing package content: $entry"
    }
    $reader = New-Object IO.StreamReader($archive.GetEntry('README.md').Open())
    try { $readme = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $prose = [regex]::Replace($readme, '(?ms)^```[^\r\n]*\r?\n.*?^```[^\r\n]*', '')
    $prose = [regex]::Replace($prose, '`[^`\r\n]*`', '')
    Assert-True (-not [regex]::IsMatch($prose, '</?[A-Za-z][^>]*>')) 'Package README must use NuGet-supported Markdown outside code examples'
}
finally {
    $archive.Dispose()
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    $feedConfig = Join-Path $testRoot 'NuGet.config'
    $feed = [Security.SecurityElement]::Escape((Resolve-Path -LiteralPath $PackageDirectory).Path)
    Set-Content -LiteralPath $feedConfig -Value "<configuration><packageSources><clear/><add key='package' value='$feed'/></packageSources></configuration>"
    $toolDir = Join-Path $testRoot 'tool'
    dotnet tool install sharplint --version $version --tool-path $toolDir --configfile $feedConfig --no-cache
    Assert-True ($LASTEXITCODE -eq 0) 'Tool installation failed'

    $toolName = if ($env:OS -eq 'Windows_NT') { 'sharplint.exe' } else { 'sharplint' }
    $tool = Join-Path $toolDir $toolName
    $reportedVersion = & $tool --version
    Assert-True ($LASTEXITCODE -eq 0 -and $reportedVersion -eq $version) 'Installed command reports the wrong version'
    $helpText = & $tool --help
    Assert-True ($LASTEXITCODE -eq 0 -and ($helpText -join "`n").Contains('Usage: sharplint')) 'Installed command help failed'

    $workspace = Join-Path $testRoot 'workspace'
    New-Item -ItemType Directory -Path $workspace | Out-Null
    $source = Join-Path $workspace 'Sample.cs'
    Set-Content -LiteralPath $source -Value 'missing'
    $server = Join-Path $repoRoot 'tests/SharpLint.FakeServer/bin/Debug/net10.0/SharpLint.FakeServer.dll'
    $result = & $tool check $source --workspace $workspace --server $server --format json
    Assert-True ($LASTEXITCODE -eq 1) "Installed command must return 1 for diagnostic errors: $result"
    $diagnostics = ($result -join "`n") | ConvertFrom-Json
    Assert-True ($diagnostics.complete -and $diagnostics.freshness -eq 'verified') 'Installed command must return complete, fresh document diagnostics'
    Assert-True ($diagnostics.diagnostics.code -contains 'CS0103') 'Installed command must return the compiler diagnostic'

    Set-Content -LiteralPath $source -Value 'fixed'
    $result = & $tool check $source --workspace $workspace --server $server --format json
    Assert-True ($LASTEXITCODE -eq 0) "Installed command must return 0 after the source is fixed: $result"
    $diagnostics = ($result -join "`n") | ConvertFrom-Json
    Assert-True ($diagnostics.complete -and $diagnostics.diagnosticCount -eq 0) 'Installed command must report the fix'
    Write-Output "sharplint $version package metadata, installation, help, version, and diagnostic checks passed."
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

exit 0
