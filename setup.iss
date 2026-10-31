#define MyAppName "CosmOrganizer"
#define MyAppVersion "3.2.1"
#define MyAppExeName "CosmOrganizer.exe"

[Setup]
AppId={{8A7B6C5D-4E3F-2A1B-9C8D-7E6F5A4B3C2D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CosmoNet.info
AppPublisherURL=https://www.cosmonet.info
AppSupportURL=https://www.cosmonet.info
AppUpdatesURL=https://github.com/CosmoNetinfo/CosmOrganizer/releases/latest
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=C:\Users\theda\Documents\CosmOrganizer\Installer
OutputBaseFilename=CosmOrganizer_Setup_v3.2.1
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=C:\Users\theda\Documents\CosmOrganizer\app.ico
UninstallDisplayIcon={app}\CosmOrganizer.exe

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea icona sul Desktop"; GroupDescription: "Icone:"

[Files]
Source: "C:\Users\theda\Documents\CosmOrganizer\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "C:\Users\theda\Documents\CosmOrganizer\logo.png"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\CosmOrganizer.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\CosmOrganizer.exe"; Tasks: desktopicon
Name: "{group}\Disinstalla {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia CosmOrganizer"; Flags: nowait postinstall skipifsilent

[Registry]
; Menu contestuale CLASSICO (Windows 10 e "Mostra altre opzioni" in Windows 11)
Root: HKCU; Subkey: "Software\Classes\Directory\shell\CosmOrganizer"; ValueType: string; ValueName: ""; ValueData: "Organizza con CosmOrganizer"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\CosmOrganizer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\CosmOrganizer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\CosmOrganizer"; ValueType: string; ValueName: ""; ValueData: "Organizza con CosmOrganizer"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\CosmOrganizer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\CosmOrganizer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%V"""

; Menu contestuale MODERNO Windows 11 (appare direttamente nel menu principale)
Root: HKCR; Subkey: "Directory\shell\CosmOrganizer"; ValueType: string; ValueName: ""; ValueData: "Organizza con CosmOrganizer"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\CosmOrganizer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"
Root: HKCR; Subkey: "Directory\shell\CosmOrganizer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

Root: HKCR; Subkey: "Directory\Background\shell\CosmOrganizer"; ValueType: string; ValueName: ""; ValueData: "Organizza con CosmOrganizer"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\CosmOrganizer"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"
Root: HKCR; Subkey: "Directory\Background\shell\CosmOrganizer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%V"""

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
