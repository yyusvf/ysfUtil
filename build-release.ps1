param([string]$Iscc)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    [xml]$project = Get-Content ysfUtil.csproj
    $version = $project.Project.PropertyGroup.Version | Where-Object { $_ }
    if (!$Iscc) {
        $candidates = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        )
        $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$Iscc) { $Iscc = (Get-Command ISCC -ErrorAction SilentlyContinue).Source }
    }
    if (!$Iscc) { throw 'Inno Setup 6 is required to build the setup. Install it from https://jrsoftware.org/isinfo.php.' }
    dotnet run --project Tests/ShutdownTimer.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish ysfUtil.csproj -c Release -o dist/packages --self-contained false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    # Setup uses the same small EXE; a missing Desktop Runtime is downloaded during installation.
    & $Iscc "/DAppVersion=$version" setup/ysfUtil.iss
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    Write-Host "Ready: dist/packages/ysfUtil-Setup.exe (recommended) and dist/packages/ysfUtil.exe (portable)."
}
finally { Pop-Location }
