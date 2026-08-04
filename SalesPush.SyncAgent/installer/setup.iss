; ============================================================
;  SalesPush Sync Agent Setup — Inno Setup 6 Script
;
;  Build steps:
;   1. Publish the agent (self-contained, so the client PC doesn't need the
;      .NET runtime pre-installed):
;        dotnet publish ..\SalesPush.SyncAgent.csproj -c Release -r win-x64 ^
;          --self-contained true -p:PublishSingleFile=true -o payload
;   2. Compile this script: ISCC.exe setup.iss
;   3. Output: Output\SalesPushSyncAgentSetup.exe
;
;  Same shape as SaleOrd.SyncAgent's installer — a single small desktop app
;  (config + scheduler UI), no IIS, no SQL Express. Auto-start-with-Windows
;  is handled by the app itself (registry Run key, toggled from its own
;  Configuration tab), not by this installer.
; ============================================================

#define MyAppName      "SalesPush Sync Agent"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "SaleOrd"
#define MyAppExeName   "SalesPush.SyncAgent.exe"

[Setup]
AppId={{6E2A9C4F-1D7B-4F3E-8A2C-9B5D6E1F3A82}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\SalesPush Sync Agent
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=SalesPushSyncAgentSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\agent.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

; ============================================================
;  Files to install
; ============================================================
[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; ============================================================
;  Shortcuts
; ============================================================
[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; ============================================================
;  Launch after install
; ============================================================
[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName} now"; \
  Flags: nowait postinstall skipifsilent

; ============================================================
;  Uninstall cleanup — remove the auto-start registry entry the app
;  registers itself (Software\Microsoft\Windows\CurrentVersion\Run), so an
;  uninstall doesn't leave a dangling shortcut to a deleted exe.
; ============================================================
[UninstallRun]
Filename: "reg.exe"; \
  Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v SalesPushSyncAgent /f"; \
  Flags: runhidden waituntilterminated; \
  RunOnceId: "RemoveAutoStart"
