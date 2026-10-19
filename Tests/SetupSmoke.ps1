# Tests the real Inno runtime checks and installation without touching the real app's settings,
# shortcuts or uninstall identity. No runtime is installed or removed by this test.
param([string]$Iscc)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
Push-Location $repo
try {
    if (!$Iscc) {
        $Iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$Iscc) { $Iscc = (Get-Command ISCC -ErrorAction Stop).Source }
    }
    [xml]$project = Get-Content ysfUtil.csproj
    $version = $project.Project.PropertyGroup.Version | Where-Object { $_ }
    $testId = [guid]::NewGuid().ToString().ToUpperInvariant()
    $original = Get-Content setup/ysfUtil.iss -Raw
    $base = $original.Substring(0, $original.IndexOf('[Code]'))
    $base = $base.Replace('DD2FE892-08F0-47C0-B7C8-053247089716', $testId).Replace('AppName=ysfUtil', 'AppName=ysfUtil Runtime Smoke Test')
    $base = [regex]::Replace($base, '(?ms)^\[Icons\].*?(?=^\[)', '')
    $base = [regex]::Replace($base, '(?ms)^\[Run\].*?(?=^\[)', '')
    Copy-Item setup/installation.txt dist/installation.txt
    $runtimePath = Join-Path $repo 'setup\DesktopRuntime.iss'
    $checks = @'
procedure Require(Value: Boolean; const Message: String);
begin
  if not Value then RaiseException(Message);
end;
procedure Fixture(const Root, Framework, Version, LibraryName: String);
var
  Directory: String;
begin
  Directory := AddBackslash(Root) + 'shared\' + Framework + '\' + Version;
  ForceDirectories(Directory);
  SaveStringToFile(AddBackslash(Directory) + LibraryName, 'fixture', False);
end;
function InitializeSetup: Boolean;
var
  Root, PreviewRoot: String;
begin
  Root := ExpandConstant('{tmp}\runtime-fixture');
  Require(not DesktopRuntimeAt(Root), 'Empty root was accepted');
  ForceDirectories(Root);
  SaveStringToFile(Root + '\dotnet.exe', 'fixture', False);
  Fixture(Root, 'Microsoft.NETCore.App', '9.0.0', 'coreclr.dll');
  Fixture(Root, 'Microsoft.WindowsDesktop.App', '9.0.0', 'PresentationFramework.dll');
  Require(not DesktopRuntimeAt(Root), '.NET 9 was accepted instead of .NET 8');
  Fixture(Root, 'Microsoft.NETCore.App', '8.0.31', 'coreclr.dll');
  Require(not DesktopRuntimeAt(Root), 'Core runtime without Desktop was accepted');
  Fixture(Root, 'Microsoft.WindowsDesktop.App', '8.0.32', 'PresentationFramework.dll');
  Require(not DesktopRuntimeAt(Root), 'Incomplete runtime with older core was accepted');
  Fixture(Root, 'Microsoft.NETCore.App', '8.0.32', 'coreclr.dll');
  Require(DesktopRuntimeAt(Root), 'Compatible stable runtimes were not accepted');
  PreviewRoot := Root + '\preview';
  ForceDirectories(PreviewRoot);
  SaveStringToFile(PreviewRoot + '\dotnet.exe', 'fixture', False);
  Fixture(PreviewRoot, 'Microsoft.NETCore.App', '8.0.32', 'coreclr.dll');
  Fixture(PreviewRoot, 'Microsoft.WindowsDesktop.App', '8.0.33-preview', 'PresentationFramework.dll');
  Require(not DesktopRuntimeAt(PreviewRoot), 'Preview Desktop Runtime was accepted');
  Require(DesktopRuntimeInstalled, 'This smoke test requires an existing .NET 8 Desktop Runtime');
  Log('PASS: runtime detection fixtures');
  Result := True;
end;
'@
    $script = $base.Replace('OutputBaseFilename=ysfUtil-Setup', 'OutputBaseFilename=ysfUtil-Runtime-Smoke') + "`n[Code]`n#include `"$runtimePath`"`n" + $checks
    Set-Content dist/runtime-smoke.iss $script
    & $Iscc /Q "/DAppVersion=$version" dist/runtime-smoke.iss
    if ($LASTEXITCODE -ne 0) { throw 'Smoke compiler failed' }
    $installDir = [IO.Path]::GetFullPath((Join-Path $repo "dist\runtime-smoke-install-$testId"))
    if (!$installDir.StartsWith($repo + '\dist\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe smoke path' }
    try {
        foreach ($step in @('install', 'update')) {
            $log = Join-Path $repo "dist\runtime-smoke-$step.log"
            $process = Start-Process (Join-Path $repo 'dist\packages\ysfUtil-Runtime-Smoke.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=', "/DIR=`"$installDir`"", "/LOG=`"$log`"") -WindowStyle Hidden -PassThru -Wait
            if ($process.ExitCode -ne 0) { throw "Smoke $step failed: $($process.ExitCode)" }
            if (!(Select-String -LiteralPath $log -SimpleMatch 'no download required')) { throw 'Existing runtime did not bypass download' }
            if (!(Test-Path (Join-Path $installDir 'ysfUtil.installation'))) { throw 'Installation marker missing' }
            Write-Host "PASS: runtime fixtures and silent $step with no runtime download"
        }
    }
    finally {
        if (Test-Path (Join-Path $installDir 'unins000.exe')) {
            $process = Start-Process (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -PassThru -Wait
            if ($process.ExitCode -ne 0) { throw 'Smoke uninstall failed' }
        }
    }
    if (Test-Path (Join-Path $installDir 'ysfUtil.exe')) { throw 'Uninstall left app behind' }
    Write-Host 'PASS: silent uninstall'

    # Force the missing-runtime branch and a connection failure. Never call Microsoft's installer.
    $missingRuntime = Get-Content setup/DesktopRuntime.iss -Raw
    $missingRuntime = [regex]::Replace($missingRuntime, '(?s)function DesktopRuntimeInstalled: Boolean;.*?(?=function OnRuntimeDownloadProgress)', "function DesktopRuntimeInstalled: Boolean;`nbegin Result := False; end;`n")
    $missingRuntime = [regex]::Replace($missingRuntime, '#define DesktopRuntimeUrl ".*"', '#define DesktopRuntimeUrl "https://127.0.0.1:1/runtime.exe"')
    Set-Content dist/DesktopRuntimeMissing.iss $missingRuntime
    $missing = $base.Replace($testId, [guid]::NewGuid().ToString().ToUpperInvariant()).Replace('OutputBaseFilename=ysfUtil-Setup', 'OutputBaseFilename=ysfUtil-Runtime-Missing-Smoke') + "`n[Code]`n#include `"DesktopRuntimeMissing.iss`"`n"
    Set-Content dist/runtime-missing-smoke.iss $missing
    & $Iscc /Q "/DAppVersion=$version" dist/runtime-missing-smoke.iss
    if ($LASTEXITCODE -ne 0) { throw 'Missing-runtime smoke compiler failed' }
    New-Item -ItemType Directory -Force $installDir | Out-Null
    Set-Content (Join-Path $installDir 'ysfUtil.exe') 'preserve previous app'
    $process = Start-Process (Join-Path $repo 'dist\packages\ysfUtil-Runtime-Missing-Smoke.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDir`"", "/LOG=`"$repo\dist\runtime-missing-smoke.log`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -eq 0 -or (Get-Content (Join-Path $installDir 'ysfUtil.exe')) -ne 'preserve previous app' -or (Test-Path (Join-Path $installDir 'ysfUtil.installation'))) {
        throw 'Runtime download failure replaced app or continued installation'
    }
    Write-Host 'PASS: missing-runtime download failure aborts before changing the app'
}
finally { Pop-Location }
