; =============================================================================
; Instalador do Quorum para Windows — Inno Setup 6
;
; Como gerar:
;   1. instale o Inno Setup (https://jrsoftware.org/isdl.php)
;   2. rode gerar_instalador.cmd (publica o app e compila este script)
;
; O executavel e self-contained: o usuario final NAO precisa do .NET instalado.
; Para as automacoes via MCP (tela/banco), precisa do Node.js 18+ — o instalador
; avisa isso na tela final em vez de embutir o Node (licenca e tamanho).
; =============================================================================

#define AppName "Quorum"
#define AppVersion "5.0.0"
#define AppPublisher "Leonardo Gonzaga"
#define AppURL "https://github.com/LJCGJ/Quorum.Studio"
#define AppExe "Quorum.App.exe"

[Setup]
; AppId fixo: e o que permite atualizar por cima sem duplicar em "Aplicativos instalados"
AppId={{C04DC8E1-1D2F-45BE-A7BF-37FACBC31CD5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\..\LICENSE
OutputDir=saida
OutputBaseFilename=Quorum-{#AppVersion}-win-x64
SetupIconFile=..\..\src\Quorum.App\Assets\quorum.ico
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; publicacao gerada por gerar_instalador.cmd em ..\publish\win-x64
Source: "..\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Aviso sobre o Node na ultima pagina: as automacoes via MCP dependem dele,
// mas Chat e teste de API funcionam sem — por isso e aviso, nao bloqueio.
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption :=
      WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Observacao: os testes de tela e banco de dados usam servidores MCP ' +
      'distribuidos via npm e precisam do Node.js 18+ instalado (nodejs.org). ' +
      'O chat e o teste de API funcionam sem ele.';
end;
