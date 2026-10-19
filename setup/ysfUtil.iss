#ifndef AppVersion
  #error AppVersion must be supplied by build-release.ps1
#endif

[Setup]
AppId={{DD2FE892-08F0-47C0-B7C8-053247089716}
AppName=ysfUtil
AppVersion={#AppVersion}
AppPublisher=Yusuf Esad Mumcu
AppPublisherURL=https://github.com/yyusvf/ysfUtil
AppSupportURL=https://github.com/yyusvf/ysfUtil/issues
AppUpdatesURL=https://github.com/yyusvf/ysfUtil/releases
DefaultDirName={localappdata}\Programs\ysfUtil
DefaultGroupName=ysfUtil
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
UsePreviousTasks=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=Local\ysfUtil.SingleInstance
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\ysfUtil.exe
SetupIconFile=..\ysfUtil.ico
LicenseFile=..\LICENSE
OutputDir=..\dist\packages
OutputBaseFilename=ysfUtil-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[CustomMessages]
english.RuntimeDownloading=Downloading Microsoft .NET 8 Desktop Runtime:
english.RuntimeInstalling=Installing Microsoft .NET 8 Desktop Runtime. Windows may ask for permission.
english.RuntimeFailed=Microsoft .NET 8 Desktop Runtime (x64) could not be installed. Check your connection and allow the Windows permission prompt, then retry Setup. Alternatively install the Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8.0.
german.RuntimeDownloading=Microsoft .NET 8 Desktop Runtime wird heruntergeladen:
german.RuntimeInstalling=Microsoft .NET 8 Desktop Runtime wird installiert. Windows fragt eventuell nach einer Berechtigung.
german.RuntimeFailed=Microsoft .NET 8 Desktop Runtime (x64) konnte nicht installiert werden. Pruefe deine Verbindung und erlaube die Windows-Berechtigungsabfrage. Starte Setup danach erneut. Alternativ: https://dotnet.microsoft.com/download/dotnet/8.0.

[Files]
Source: "..\dist\packages\ysfUtil.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "installation.txt"; DestDir: "{app}"; DestName: "ysfUtil.installation"; Flags: ignoreversion

[Icons]
Name: "{group}\ysfUtil"; Filename: "{app}\ysfUtil.exe"
Name: "{autodesktop}\ysfUtil"; Filename: "{app}\ysfUtil.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ysfUtil.exe"; Parameters: "--update-show"; Description: "{cm:LaunchProgram,ysfUtil}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\ysfUtil.exe.previous"
Type: files; Name: "{app}\ysfUtil.exe.incoming"

[Code]
#include "DesktopRuntime.iss"

const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

// Preserve the user's choice in the app; only move an existing autostart entry.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Existing: String;
begin
  if (CurStep = ssPostInstall) and RegQueryStringValue(HKCU, RunKey, 'ysfUtil', Existing) then
    RegWriteStringValue(HKCU, RunKey, 'ysfUtil', '"' + ExpandConstant('{app}\ysfUtil.exe') + '" --tray');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Existing: String;
begin
  if (CurUninstallStep = usUninstall) and RegQueryStringValue(HKCU, RunKey, 'ysfUtil', Existing) then
    if CompareText(Existing, '"' + ExpandConstant('{app}\ysfUtil.exe') + '" --tray') = 0 then
      RegDeleteValue(HKCU, RunKey, 'ysfUtil');
  // User settings in AppData intentionally survive uninstall and reinstall.
end;
