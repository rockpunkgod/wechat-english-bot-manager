#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppExe
  #error AppExe is required
#endif
#ifndef MaxInstallPath
  #error MaxInstallPath is required
#endif
#define AppName "微信英语Bot管理器"
#define ExeName "微信英语Bot管理器-用量整合版.exe"

[Setup]
#ifdef TestBuild
AppId=Rockpunkgod.WechatEnglishBotManager.SetupTest
CreateUninstallRegKey=no
OutputBaseFilename=WechatEnglishBotManager-TestSetup-{#AppVersion}-x64
#else
AppId=Rockpunkgod.WechatEnglishBotManager
OutputBaseFilename=WechatEnglishBotManager-Setup-{#AppVersion}-x64
#endif
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=rockpunkgod
AppPublisherURL=https://github.com/rockpunkgod/wechat-english-bot-manager
AppUpdatesURL=https://github.com/rockpunkgod/wechat-english-bot-manager/releases
DefaultDirName={code:GetDefaultDirectory}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\assets\bot-manager-v1.ico
UninstallDisplayIcon={app}\{#ExeName}
UninstallFilesDir={app}\uninstall
Compression=lzma2
SolidCompression=yes
OutputDir=..\release
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
SetupMutex=Rockpunkgod.WechatEnglishBotManager.Setup
AllowRootDirectory=no
AllowUNCPath=no
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "zhCN"; MessagesFile: "compiler:Default.isl"

[LangOptions]
LanguageName=简体中文
LanguageID=$0804
LanguageCodePage=0
DialogFontName=Microsoft YaHei UI
WelcomeFontName=Microsoft YaHei UI

[Messages]
SetupAppTitle=安装程序
SetupWindowTitle=安装 - %1
UninstallAppTitle=卸载程序
UninstallAppFullTitle=卸载 %1
InformationTitle=提示
ConfirmTitle=确认
ErrorTitle=错误
ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是(&Y)
ButtonNo=否(&N)
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&B)...
ButtonWizardBrowse=浏览(&R)...
WelcomeLabel1=欢迎安装 [name]
WelcomeLabel2=即将安装 [name/ver]。%n%n升级时请选择原管理器所在目录，继续使用原有账号与用量数据。%n%n安装包内置运行环境；使用 Bot 前仍需在本机安装并登录 Codex。
ClickNext=点击“下一步”继续，或点击“取消”退出。
WizardSelectDir=选择安装位置
SelectDirDesc=将 [name] 安装到哪里？
SelectDirLabel3=程序将安装到以下文件夹。升级请保持原目录。
SelectDirBrowseLabel=点击“下一步”继续；点击“浏览”可更换目录。
DiskSpaceMBLabel=至少需要 [mb] MB 可用磁盘空间。
WizardSelectTasks=选择附加任务
SelectTasksDesc=需要创建哪些快捷方式？
SelectTasksLabel2=选择附加任务，然后点击“下一步”。
WizardReady=准备安装
ReadyLabel1=现在可以在计算机上安装 [name]。
ReadyLabel2a=点击“安装”开始。点击“上一步”可检查或更改设置。
ReadyMemoDir=安装位置：
ReadyMemoTasks=附加任务：
WizardPreparing=准备安装
PreparingDesc=正在检查安装条件。
WizardInstalling=正在安装
InstallingLabel=正在安装 [name]，请稍候。
StatusExtractFiles=正在写入程序文件...
StatusCreateIcons=正在创建快捷方式...
StatusRunProgram=正在启动程序...
FinishedHeadingLabel=安装完成
FinishedLabelNoIcons=[name] 已安装。原有账号、用量与运行目录均保留。
FinishedLabel=[name] 已安装。可通过桌面或开始菜单打开管理器。
ClickFinish=点击“完成”关闭安装程序。
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成。确定退出吗？
ConfirmUninstall=确定卸载 %1 吗？账号、用量数据及运行目录会保留。
UninstallStatusLabel=正在卸载 %1，请稍候。
UninstalledAll=%1 已卸载。账号和用量数据仍保留在原安装目录。
ApplicationsFound=以下程序正在使用需要更新的文件。请选择关闭这些程序后继续。
ApplicationsFound2=以下程序正在使用需要更新的文件。
CloseApplications=自动关闭这些程序(&A)
DontCloseApplications=暂不关闭这些程序(&D)

#ifndef TestBuild
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Icons]
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userprograms}\{#AppName}\{#AppName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{userprograms}\{#AppName}\卸载管理器"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#ExeName}"; Description: "打开微信英语 Bot 管理器"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked
#endif

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; DestName: "{#ExeName}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "使用说明-{#AppVersion}.md"; Flags: ignoreversion

; Runtime data and extracted engines are never installer-owned files.
; No recursive InstallDelete or UninstallDelete entries.
[Code]
function GetDefaultDirectory(Param: String): String;
begin
  if DirExists('D:\') then
    Result := 'D:\bot\release'
  else
    Result := ExpandConstant('{localappdata}\Programs\WechatEnglishBotManager');
end;

procedure RegisterExtraCloseApplicationsResources;
begin
  RegisterExtraCloseApplicationsResource(False, ExpandConstant('{app}\微信英语Bot管理器.exe'));
  RegisterExtraCloseApplicationsResource(False, ExpandConstant('{app}\微信英语Bot管理器-新图标.exe'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Length(ExpandConstant('{app}')) > {#MaxInstallPath} then
    Result := '安装目录过长，首次启动可能无法解压运行环境。请返回并选择较短的目录，例如 D:\bot\release。';
end;
