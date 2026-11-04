# Resolve the MSVC and Windows SDK import libraries for a normal PowerShell session.
if ([Environment]::OSVersion.Platform -eq 'Win32NT' -and -not $env:LIB) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vs) {
            $msvc = Get-ChildItem -LiteralPath (Join-Path $vs 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
            $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Lib'
            $sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
            if ($msvc -and $sdk) {
                $env:LIB = @((Join-Path $msvc.FullName 'lib/x64'), (Join-Path $sdk.FullName 'ucrt/x64'), (Join-Path $sdk.FullName 'um/x64')) -join ';'
            }
        }
    }
}
