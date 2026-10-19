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
    dotnet publish ysfUtil.csproj -c Release -o dist/installer-app --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Setup payload publish failed.' }
    # Preserve the licenses of the runtime packs bundled into the setup payload.
    $cache = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
    $deps = Get-Content bin/Release/net8.0-windows/win-x64/ysfUtil.deps.json -Raw | ConvertFrom-Json
    New-Item -ItemType Directory -Force dist/installer-app/licenses | Out-Null
    foreach ($pack in $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' }) {
        $parts = $pack.Substring('runtimepack.'.Length).Split('/')
        $packRoot = Join-Path $cache ($parts[0].ToLowerInvariant() + '/' + $parts[1])
        $license = @('LICENSE.TXT', 'LICENSE') | ForEach-Object { Join-Path $packRoot $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$license) { throw "Runtime license missing: $pack" }
        Copy-Item -LiteralPath $license -Destination "dist/installer-app/licenses/$($parts[0])-LICENSE.txt"
        $notices = Join-Path $packRoot 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination "dist/installer-app/licenses/$($parts[0])-NOTICES.txt" }
    }
    & $Iscc "/DAppVersion=$version" setup/ysfUtil.iss
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    Write-Host "Ready: dist/packages/ysfUtil-Setup.exe (recommended) and dist/packages/ysfUtil.exe (portable)."
}
finally { Pop-Location }
