#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
[Setup]
AppId={{36864DCB-6DCF-4A87-85B5-B39C4A395001}
AppName=Wire Marker Inspection
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\WireMarkerInspection
DefaultGroupName=Wire Marker Inspection
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=WireMarkerInspection-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\WireMarkerInspection.Desktop.exe
[Files]
Source: "..\publish\WireMarkerInspection\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Wire Marker Inspection"; Filename: "{app}\WireMarkerInspection.Desktop.exe"
[Run]
Filename: "{app}\WireMarkerInspection.Desktop.exe"; Description: "Open Wire Marker Inspection"; Flags: nowait postinstall skipifsilent
