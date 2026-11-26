; Inno Setup — Qolome Devtoy
; 构建: ISCC.exe /DMyAppVersion=1.1.0 packaging\inno\Qolome.Devtoy.iss
; 或由 packaging\package.ps1 自动调用（传入 /D 覆盖路径）

#ifndef MyAppVersion
#define MyAppVersion "1.1.0"
#endif

#ifndef SourceDir
#define SourceDir "..\..\dist\publish\win-x64"
#endif

#ifndef OutputDirPath
#define OutputDirPath "..\..\dist\release"
#endif

#ifndef SetupIconPath
#define SetupIconPath "..\..\icons\app.ico"
#endif

#define MyAppFolder "Qolome Devtoy"
#define MyAppDisplayName "Qolome Devtoy"
#define MyAppPublisher "Qolome"
#define MyAppExeName "Qolome.Devtoy.App.exe"
#define MyAppUrl "https://qolome.com"

[Setup]
AppId={{A7C3E9F1-2B4D-5E6A-8C0D-1E2F3A4B5C6D}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppDisplayName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={autopf}\{#MyAppFolder}
DefaultGroupName={#MyAppFolder}
AllowNoIcons=yes
OutputDir={#OutputDirPath}
OutputBaseFilename=Qolome.Devtoy_{#MyAppVersion}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Inno Setup 6.2 使用 x64；6.3+ 的 x64compatible 在此版本无效
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#SetupIconPath}
DisableProgramGroupPage=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDisplayName} 安装程序
VersionInfoProductName={#MyAppDisplayName}
VersionInfoCopyright=Copyright (C) {#MyAppPublisher}

[Languages]
; 汉化版 Inno 常无 compiler:Languages\ChineseSimplified.isl，改用仓库内语言包
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\卸载 {#MyAppDisplayName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppDisplayName}"; Flags: nowait postinstall skipifsilent