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

    $copilotConfig = Join-Path $testRoot 'copilot/mcp-config.json'
    $codeConfig = Join-Path $testRoot 'vscode/mcp.json'
    & $tool setup copilot --copilot-config $copilotConfig --vscode-config $codeConfig
    Assert-True ($LASTEXITCODE -eq 0) 'Installed agent setup command failed'
    $copilot = Get-Content -LiteralPath $copilotConfig -Raw | ConvertFrom-Json
    $code = Get-Content -LiteralPath $codeConfig -Raw | ConvertFrom-Json
    Assert-True ($copilot.mcpServers.sharplint.args -contains 'mcp' -and $code.servers.sharplint.args -contains 'mcp') 'Both clients must start MCP'
    Assert-True ($copilot.mcpServers.sharplint.args -notcontains '--workspace' -and $code.servers.sharplint.args -notcontains '--workspace') 'Agent setup must be repository-independent'

    $workspace = Join-Path $testRoot 'workspace'
    New-Item -ItemType Directory -Path $workspace | Out-Null
    $source = Join-Path $workspace 'Sample.cs'
    Set-Content -LiteralPath $source -Value 'missing'
    $server = Join-Path $repoRoot 'tests/SharpLint.FakeServer/bin/Debug/net10.0/SharpLint.FakeServer.dll'
    $mcp = New-Object Diagnostics.Process
    $mcp.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $mcp.StartInfo.FileName = $tool
    $mcp.StartInfo.UseShellExecute = $false
    $mcp.StartInfo.RedirectStandardInput = $true
    $mcp.StartInfo.RedirectStandardOutput = $true
    $mcp.StartInfo.RedirectStandardError = $true
    foreach ($argument in @('mcp', '--server', $server)) { $mcp.StartInfo.ArgumentList.Add($argument) }
    Assert-True ($mcp.Start()) 'Installed MCP server failed to start'
    $mcp.StandardInput.AutoFlush = $true
    $stderr = $mcp.StandardError.ReadToEndAsync()
    $mcpState = [PSCustomObject]@{ Id = 0; Process = $mcp }
    function Invoke-McpRequest([string] $Method, [hashtable] $Parameters) {
        $mcpState.Id++
        $message = @{ jsonrpc = '2.0'; id = $mcpState.Id; method = $Method; params = $Parameters }
        $mcpState.Process.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 30 -Compress))
        while ($true) {
            $read = $mcpState.Process.StandardOutput.ReadLineAsync()
            Assert-True ($read.Wait(30000)) "MCP response timed out: $Method"
            $line = $read.GetAwaiter().GetResult()
            Assert-True ($null -ne $line) "MCP server disconnected: $Method"
            $reply = $line | ConvertFrom-Json
            if ($reply.id -eq $mcpState.Id) {
                Assert-True ($null -eq $reply.error) "MCP request failed: $line"
                Assert-True (-not $reply.result.isError) "MCP tool failed: $line"
                return $reply.result
            }
        }
    }
    try {
        $null = Invoke-McpRequest 'initialize' @{ protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'sharplint-package-check'; version = '1' } }
        $mcp.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
        $tools = Invoke-McpRequest 'tools/list' @{}
        Assert-True ($tools.tools.name -contains 'sharplint_check' -and $tools.tools.name -contains 'sharplint_status') 'Installed MCP tools are missing'
        $status = (Invoke-McpRequest 'tools/call' @{ name = 'sharplint_status'; arguments = @{ workspace = $workspace } }).structuredContent
        Assert-True ($status.available) 'Installed MCP server must initialize Roslyn'
        $request = @{ name = 'sharplint_check'; arguments = @{ workspace = $workspace; path = 'Sample.cs' } }
        $diagnostics = (Invoke-McpRequest 'tools/call' $request).structuredContent
        Assert-True ($diagnostics.complete -and $diagnostics.freshness -eq 'verified') 'Installed MCP tool must return fresh document diagnostics'
        Assert-True ($diagnostics.diagnostics.code -contains 'CS0103') 'Installed MCP tool must return compiler feedback'
        Set-Content -LiteralPath $source -Value 'fixed'
        $diagnostics = (Invoke-McpRequest 'tools/call' $request).structuredContent
        Assert-True ($diagnostics.complete -and $diagnostics.diagnosticCount -eq 0) 'Installed MCP tool must report the fix'
        $again = (Invoke-McpRequest 'tools/call' @{ name = 'sharplint_status'; arguments = @{ workspace = $workspace } }).structuredContent
        Assert-True ($status.processId -eq $again.processId) 'Installed MCP server must reuse the warm Roslyn process'
        Write-Output "sharplint $version package metadata, installation, help, version, and MCP session checks passed."
    }
    finally {
        $mcp.StandardInput.Close()
        if (-not $mcp.WaitForExit(10000)) { $mcp.Kill($true); $mcp.WaitForExit() }
        if ($mcp.ExitCode -ne 0) { [Console]::Error.WriteLine($stderr.GetAwaiter().GetResult()) }
        $mcp.Dispose()
    }
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

exit 0
