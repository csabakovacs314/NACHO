; NACHO installer, built with Inno Setup 6.7.
; Installs for the current user by default (no admin rights); "all users" can be chosen in the first dialog.
; English and Spanish. Compile: ISCC.exe installer\nacho.iss (after publishing the payload, see build.ps1).

#define AppVersion "0.2.0"
#ifndef Payload
  #define Payload "..\out\installer-payload"
#endif

[Setup]
AppId={{A0A39F9F-4BE7-4092-8791-E3554BC5B9E7}
AppName=NACHO
AppVersion={#AppVersion}
AppVerName=NACHO {#AppVersion}
AppPublisher=Csaba Kovacs
AppPublisherURL=https://github.com/csabakovacs314/NACHO
AppSupportURL=https://github.com/csabakovacs314/NACHO/issues
AppComments=Normalise, Adjust, Compress, Hand Over
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=NACHO setup
DefaultDirName={autopf}\NACHO
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out\installer
OutputBaseFilename=NACHO-{#AppVersion}-setup
SetupIconFile=..\src\Nacho\nacho.ico
UninstallDisplayIcon={app}\NACHO.exe
UninstallDisplayName=NACHO
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.GroupShell=Windows integration:
es.GroupShell=Integración con Windows:
en.TaskContextMenu=Add "Make Docs-ready" to the right-click menu of images
es.TaskContextMenu=Añadir "Preparar para Docs" al menú del clic derecho de las imágenes
en.TaskSendTo=Add NACHO to the Send to menu
es.TaskSendTo=Añadir NACHO al menú Enviar a
en.MenuText=Make Docs-ready
es.MenuText=Preparar para Docs
en.AppComment=Docs-ready images: Normalise, Adjust, Compress, Hand Over
es.AppComment=Imágenes listas para Docs: Normaliza, Ajusta, Comprime y Honra el Original

[Tasks]
Name: "contextmenu"; Description: "{cm:TaskContextMenu}"; GroupDescription: "{cm:GroupShell}"
Name: "sendto"; Description: "{cm:TaskSendTo}"; GroupDescription: "{cm:GroupShell}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\NACHO"; Filename: "{app}\NACHO.exe"; Comment: "{cm:AppComment}"
Name: "{autodesktop}\NACHO"; Filename: "{app}\NACHO.exe"; Comment: "{cm:AppComment}"; Tasks: desktopicon
Name: "{usersendto}\NACHO"; Filename: "{app}\NACHO.exe"; Comment: "{cm:AppComment}"; Tasks: sendto

[Registry]
; The right-click entry: current user only, the same keys NACHO's More options writes.
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuText}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:MenuText}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\NACHO.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\NACHO.MakeDocsReady\command"; ValueType: string; ValueName: ""; ValueData: """{app}\NACHO.exe"" ""%1"""; Tasks: contextmenu

Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heic\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuText}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heic\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:MenuText}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heic\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\NACHO.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heic\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heic\shell\NACHO.MakeDocsReady\command"; ValueType: string; ValueName: ""; ValueData: """{app}\NACHO.exe"" ""%1"""; Tasks: contextmenu

Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuText}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:MenuText}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\NACHO.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.heif\shell\NACHO.MakeDocsReady\command"; ValueType: string; ValueName: ""; ValueData: """{app}\NACHO.exe"" ""%1"""; Tasks: contextmenu

Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuText}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:MenuText}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\NACHO.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\NACHO.MakeDocsReady\command"; ValueType: string; ValueName: ""; ValueData: """{app}\NACHO.exe"" ""%1"""; Tasks: contextmenu

Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuText}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:MenuText}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\NACHO.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avif\shell\NACHO.MakeDocsReady"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avif\shell\NACHO.MakeDocsReady\command"; ValueType: string; ValueName: ""; ValueData: """{app}\NACHO.exe"" ""%1"""; Tasks: contextmenu

; The language picked in setup; NACHO uses it on its first run.
Root: HKCU; Subkey: "Software\NACHO"; ValueType: string; ValueName: "Language"; ValueData: "{language}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\NACHO.exe"; Description: "{cm:LaunchProgram,NACHO}"; Flags: nowait postinstall skipifsilent

[Code]
const
  ShellVerb = 'NACHO.MakeDocsReady';

function ShellKey(Base: String): String;
begin
  Result := 'Software\Classes\SystemFileAssociations\' + Base + '\shell\' + ShellVerb;
end;

{ Removes the right-click entry whoever made it: this installer or NACHO's More options. }
procedure RemoveContextMenu();
begin
  RegDeleteKeyIncludingSubkeys(HKCU, ShellKey('image'));
  RegDeleteKeyIncludingSubkeys(HKCU, ShellKey('.heic'));
  RegDeleteKeyIncludingSubkeys(HKCU, ShellKey('.heif'));
  RegDeleteKeyIncludingSubkeys(HKCU, ShellKey('.webp'));
  RegDeleteKeyIncludingSubkeys(HKCU, ShellKey('.avif'));
end;

procedure RemoveSendTo();
begin
  DeleteFile(ExpandConstant('{usersendto}\NACHO.lnk'));
end;

{ After installing, an unticked task means "off", even if an older install or NACHO turned it on. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not WizardIsTaskSelected('contextmenu') then
      RemoveContextMenu();
    if not WizardIsTaskSelected('sendto') then
      RemoveSendTo();
  end;
end;

{ Uninstall leaves nothing in Explorer behind. Settings in %APPDATA%\NACHO and your Docs-Ready pictures stay. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveContextMenu();
    RemoveSendTo();
  end;
end;
