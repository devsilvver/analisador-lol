; Instalador do Analisador LoL — criado por Guilherme Silvestrini (github.com/devsilvver)
; Compilar: ISCC.exe AnalisadorLoL.iss  (gera Saida\AnalisadorLoL-Instalador-<versão>.exe)

#define AppName "Analisador LoL"
#define AppVersion "1.2.2"
#define Author "Guilherme Silvestrini"
#define AuthorUrl "https://github.com/devsilvver"
#define AppExe "AnalisadorLoL.exe"

[Setup]
AppId={{6C1F3B7A-2E4D-4F1B-9A7E-8D3C5B2A1F90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Author}
AppPublisherURL={#AuthorUrl}
AppSupportURL={#AuthorUrl}
AppCopyright=© 2026 {#Author}
AppComments=Assistente leve para League of Legends — criado por {#Author}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#Author}
VersionInfoCopyright=© 2026 {#Author}
VersionInfoDescription=Instalador do {#AppName} — criado por {#Author}
VersionInfoProductName={#AppName}
; Instala só para o usuário: não pede administrador
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableWelcomePage=no
InfoBeforeFile=creditos.txt
SetupIconFile=..\assets\icone.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} (por {#Author})
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=Saida
OutputBaseFilename=AnalisadorLoL-Instalador-{#AppVersion}
; Se o programa estiver aberto, o instalador pede para fechá-lo
AppMutex=AnalisadorLoL_instancia_unica
CloseApplications=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WelcomeLabel1=Bem-vindo ao instalador do [name]
ptbr.WelcomeLabel2=Este assistente vai instalar o [name/ver] no seu computador.%n%nCriado por Guilherme Silvestrini%ngithub.com/devsilvver%n%nUm assistente leve para League of Legends: sugestões de campeões, counters, runas, builds e informações da partida em tempo real.
ptbr.FinishedHeadingLabel=Instalação concluída
ptbr.FinishedLabel=O [name] foi instalado.%n%nEle fica na bandeja do Windows (perto do relógio) e o Ctrl+X mostra ou esconde o programa durante o jogo.%n%nCriado por Guilherme Silvestrini — github.com/devsilvver%nBom jogo!
ptbr.InfoBeforeLabel=Sobre o programa e os créditos:
ptbr.InfoBeforeClickLabel=Quando estiver pronto, clique em Avançar.

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "creditos.txt"; DestDir: "{app}"; DestName: "Créditos.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Criado por {#Author}"
Name: "{group}\Créditos"; Filename: "{app}\Créditos.txt"
Name: "{group}\Desinstalar o {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Comment: "Criado por {#Author}"

[Registry]
; Inicia com o Windows, já escondido na bandeja (dá para desligar em Ajustes). Sai ao desinstalar.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AnalisadorLoL"; ValueData: """{app}\{#AppExe}"" --bandeja"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName} agora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "FecharAnalisador"

[Code]
// O programa usa o Microsoft Edge WebView2 (já vem no Windows 10/11 atualizado). Se faltar, oferece o download oficial.
function WebView2Instalado: Boolean;
var v: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v)
         or RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v);
  Result := Result and (v <> '') and (v <> '0.0.0.0');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Erro: Integer;
begin
  if (CurStep = ssPostInstall) and (not WebView2Instalado) and (not WizardSilent) then
    if MsgBox('O Analisador LoL precisa do Microsoft Edge WebView2, que não foi encontrado neste computador.' + #13#10#13#10 +
              'Deseja abrir a página oficial da Microsoft para baixá-lo?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://go.microsoft.com/fwlink/p/?LinkId=2124703', '', '', SW_SHOWNORMAL, ewNoWait, Erro);
end;
