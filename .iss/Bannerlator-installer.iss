; Offline installer for the unofficial Bannerlator ARM64 beta fork.
; Compile with ISCC /dPayloadDir=<absolute staged build path> this-file.iss
#ifndef PayloadDir
  #error PayloadDir must point to a self-contained win-arm64 publish directory
#endif

#define ForkName "7th Heaven ARM for Bannerlator"
#define ForkVersion "0.7.0"

[Setup]
AppId={{0D8DB2FA-3F34-4AE3-86B9-94976A5094E9}
AppName={#ForkName}
AppVersion={#ForkVersion}
AppVerName={#ForkName} v{#ForkVersion} beta
AppPublisher=Cyan
AppCopyright=Original 7th Heaven copyright Tsunamods and contributors; fork changes Cyan
DefaultDirName={localappdata}\Programs\7th Heaven ARM
DefaultGroupName={#ForkName}
PrivilegesRequired=lowest
OutputDir=..\.dist
OutputBaseFilename=7thHeaven-ARM-v0.7.0-beta-setup
Compression=lzma2
SolidCompression=yes
VersionInfoVersion=0.7.0.0
VersionInfoProductName={#ForkName}
VersionInfoCompany=Cyan
LicenseFile={#PayloadDir}\LICENSE.txt
UninstallDisplayName={#ForkName} v{#ForkVersion} beta
UninstallDisplayIcon={app}\7th Heaven.exe
WizardStyle=modern

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\7th Heaven ARM for Bannerlator"; Filename: "{app}\7th Heaven.exe"
Name: "{group}\README and setup guide"; Filename: "{app}\README.md"
Name: "{group}\Uninstall 7th Heaven ARM"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\7th Heaven.exe"; Description: "Launch 7th Heaven ARM"; Flags: nowait postinstall skipifsilent unchecked
