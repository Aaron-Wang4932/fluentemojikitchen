#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish\win-x64"
#endif

[Setup]
; Keep this ID unchanged for every future release so updates share one uninstall entry.
AppId={{79CB6F49-01F5-4728-8E54-23BDE5AF3C78}
AppName=Fluent Emoji Kitchen
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\FluentEmojiKitchen
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=output
OutputBaseFilename=FluentEmojiKitchen-Setup-{#AppVersion}-x64
SetupIconFile=..\Assets\Icons\AppIcon.ico
UninstallDisplayIcon={app}\FluentEmojiKitchen.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start with Windows (runs in the system tray)"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Fluent Emoji Kitchen"; Filename: "{app}\FluentEmojiKitchen.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Fluent Emoji Kitchen"; Filename: "{app}\FluentEmojiKitchen.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Fluent Emoji Kitchen"; Filename: "{app}\FluentEmojiKitchen.exe"; WorkingDir: "{app}"; Tasks: startup

[InstallDelete]
; Remove our optional shortcuts if deselected during an update.
Type: files; Name: "{userstartup}\Fluent Emoji Kitchen.lnk"; Tasks: not startup
Type: files; Name: "{autodesktop}\Fluent Emoji Kitchen.lnk"; Tasks: not desktopicon

[Run]
Filename: "{app}\FluentEmojiKitchen.exe"; Description: "Start Fluent Emoji Kitchen in the system tray"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked
