; Instalador do Bottleneck Hunter (Inno Setup 6).
; Gerado pelo GitHub Actions depois do "dotnet publish" em dist\BottleneckHunter.
; Para gerar à mão no Windows: iscc /DAppVersion=0.1.0 installer\BottleneckHunter.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "Bottleneck Hunter"
#define AppExe "BottleneckHunter.exe"

[Setup]
; O AppId identifica o app para atualizações e desinstalação: nunca mude.
AppId={{6F3B2C1E-8A4D-4E7B-9C21-B07713F0A5D1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Bottleneck Hunter
AppPublisherURL=https://github.com/thalele88/bottleneck-hunter
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=BottleneckHunter-Setup
SetupIconFile=..\src\BottleneckHunter\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Fecha o app aberto antes de atualizar os arquivos.
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
ptbr.PawnIOTask=Instalar o driver de sensores PawnIO (necessário para ver a temperatura da CPU)
ptbr.PawnIOStatus=Instalando o driver de sensores PawnIO...
ptbr.OpenNow=Abrir o Bottleneck Hunter agora

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "pawnio"; Description: "{cm:PawnIOTask}"; Check: WingetAvailable

[Files]
Source: "..\dist\BottleneckHunter\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{code:WingetPath}"; Parameters: "install --id namazso.PawnIO -e --silent --accept-package-agreements --accept-source-agreements"; \
  StatusMsg: "{cm:PawnIOStatus}"; Flags: runhidden waituntilterminated; Tasks: pawnio
Filename: "{app}\{#AppExe}"; Description: "{cm:OpenNow}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
// O winget é instalado por usuário; procura no caminho padrão do App Installer.
function WingetPath(Param: String): String;
begin
  Result := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
end;

function WingetAvailable: Boolean;
begin
  Result := FileExists(WingetPath(''));
end;
