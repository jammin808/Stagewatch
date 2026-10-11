; Inno Setup script for Stagewatch.
; Build with the Inno Setup 6 IDE or `iscc Stagewatch.iss`.

#define AppName       "Stagewatch"
#define AppVersion    "2.16.0"
#define AppPublisher  "Stagewatch"
#define AppExe        "Stagewatch.exe"
#define AppId         "{{8E2C5F60-9B5C-4F44-9D60-1F0B6E3E7B92}"
; Detect the VSTO add-in build output. If absent, the installer omits the
; PowerPoint section entirely so it still compiles before VSTO is built.
#define VstoDir "..\src\Timer.PowerPoint\bin\Release\net472"
#define HasVsto FileExists(AddBackslash(SourcePath) + VstoDir + "\Stagewatch.PowerPoint.vsto")

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Stagewatch
DefaultGroupName=Stagewatch
DisableDirPage=auto
DisableProgramGroupPage=yes
; No admin needed: the user picks "only for me" (per-user, no UAC) or "all users" (asks for admin).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
UsedUserAreasWarning=no
ChangesAssociations=yes
; 64-bit app: "all users" installs go to Program Files, not Program Files (x86).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=Stagewatch-Setup-{#AppVersion}
VersionInfoVersion={#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Timer.App\Resources\stagewatch.ico
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts"
Name: "presenterpc"; Description: "This is a presenter PC: start Stagewatch Slides now and with Windows (it sends the PowerPoint slideshow to the Stagewatch PC)"; GroupDescription: "Presenter PC"; Flags: unchecked
#if HasVsto
Name: "ppt_addin"; Description: "Install PowerPoint add-in (Pro feature)"; GroupDescription: "Optional components"
#endif

[Files]
; Main app
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion

; Bitfocus Companion module (import it in Companion → Modules → Import module package)
Source: "..\companion-module\stagewatch-*.tgz"; DestDir: "{app}\Companion"; Flags: ignoreversion skipifsourcedoesntexist

#if HasVsto
; PowerPoint VSTO add-in (Pro tier; see [Tasks])
Source: "{#VstoDir}\*.vsto";     DestDir: "{userappdata}\Stagewatch\PowerPointAddIn"; Tasks: ppt_addin
Source: "{#VstoDir}\*.dll";      DestDir: "{userappdata}\Stagewatch\PowerPointAddIn"; Tasks: ppt_addin
Source: "{#VstoDir}\*.manifest"; DestDir: "{userappdata}\Stagewatch\PowerPointAddIn"; Tasks: ppt_addin
#endif

[Icons]
Name: "{group}\Stagewatch"; Filename: "{app}\{#AppExe}"
Name: "{group}\Stagewatch Slides (presenter PC)"; Filename: "{app}\{#AppExe}"; Parameters: "--slides"; Comment: "Send this PC's PowerPoint slideshow to Stagewatch"
Name: "{autodesktop}\Stagewatch"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

#if HasVsto
[Registry]
; Register the PowerPoint add-in for the current user (LoadBehavior = 3 = load on startup)
Root: HKCU; Subkey: "Software\Microsoft\Office\PowerPoint\Addins\Stagewatch.PowerPoint"; \
   ValueType: string; ValueName: "Description"; ValueData: "Stagewatch timer overlay"; Tasks: ppt_addin
Root: HKCU; Subkey: "Software\Microsoft\Office\PowerPoint\Addins\Stagewatch.PowerPoint"; \
   ValueType: string; ValueName: "FriendlyName"; ValueData: "Stagewatch"; Tasks: ppt_addin
Root: HKCU; Subkey: "Software\Microsoft\Office\PowerPoint\Addins\Stagewatch.PowerPoint"; \
   ValueType: dword;  ValueName: "LoadBehavior"; ValueData: "3"; Tasks: ppt_addin
Root: HKCU; Subkey: "Software\Microsoft\Office\PowerPoint\Addins\Stagewatch.PowerPoint"; \
   ValueType: string; ValueName: "Manifest"; \
   ValueData: "file:///{userappdata}\Stagewatch\PowerPointAddIn\Stagewatch.PowerPoint.vsto|vstolocal"; Tasks: ppt_addin
#endif

[Registry]
; Presenter PC: Stagewatch Slides starts with Windows for this user (it can be switched off in its window).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Stagewatch Slides"; \
   ValueData: """{app}\{#AppExe}"" --slides"; Flags: uninsdeletevalue; Tasks: presenterpc
; Double-click a .stagewatch show file to open it (HKA = this user, or all users when installed for all).
Root: HKA; Subkey: "Software\Classes\.stagewatch"; ValueType: string; ValueName: ""; ValueData: "Stagewatch.Show"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Stagewatch.Show"; ValueType: string; ValueName: ""; ValueData: "Stagewatch show"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Stagewatch.Show\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\Stagewatch.Show\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
; All-users installs (admin): allow Stagewatch on private and domain networks up front, so the Windows
; Firewall prompt can't appear mid-show (remote pages, Companion, OSC, multi-room). Per-user installs
; have no admin rights and get the usual one-time prompt instead.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Stagewatch"" program=""{app}\{#AppExe}"""; Flags: runhidden; Check: IsAdminInstallMode
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Stagewatch"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=private,domain"; Flags: runhidden; Check: IsAdminInstallMode
Filename: "{app}\{#AppExe}"; Description: "Launch Stagewatch"; Flags: nowait postinstall skipifsilent; Check: not WizardIsTaskSelected('presenterpc')
Filename: "{app}\{#AppExe}"; Parameters: "--slides"; Description: "Start Stagewatch Slides"; Flags: nowait postinstall skipifsilent; Tasks: presenterpc

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Stagewatch"" program=""{app}\{#AppExe}"""; Flags: runhidden; Check: IsAdminInstallMode; RunOnceId: "StagewatchFirewall"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Stagewatch\PowerPointAddIn"
