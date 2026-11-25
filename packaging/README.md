# Qolome Devtoy 打包

发布制品默认输出到 `dist\release\`（中间 publish 仍在 `dist\publish\`）。

## 一键脚本

在仓库根目录执行（需已安装 [.NET SDK](https://dotnet.microsoft.com/download)）：

```powershell
# 在仓库根目录
.\packaging\package.ps1
```

| 参数 | 说明 |
|------|------|
| `-Target All` | 默认：ZIP + MSIX + Inno |
| `-Target Zip` | 仅便携 ZIP |
| `-Target Msix` | 仅 MSIX |
| `-Target Inno` | 仅 Inno Setup（需先 publish 或走 `Zip`/`All`） |
| `-Version 1.0.1` | 覆盖版本号（默认读 csproj） |
| `-SkipPublish` | 跳过 publish，用已有 `dist\publish\win-x64` |
| `-RequireSigning` | 未配置企业 PFX 时直接失败 |
| `-SkipSigning` | 强制不打签名（内测未签名包） |
| `-UseTestCertificate` | 仅调试；须 `QOLOME_ALLOW_TEST_CERT=1` |

## 产出物

| 文件 | 用途 |
|------|------|
| `dist\release\Qolome.Devtoy_{version}_win-x64_portable.zip` | **便携版**：解压后运行 `Qolome.Devtoy.App.exe`，删文件夹即卸载 |
| `dist\release\Qolome.Devtoy_{version}_x64.msix` | **MSIX**：可侧载安装/卸载 |
| `dist\release\Qolome.Devtoy_{version}_Setup.exe` | **Inno 安装程序**：安装向导 + 卸载项 |

## MSIX 安装 / 卸载

```powershell
Add-AppxPackage -Path "dist\release\Qolome.Devtoy_1.0.0_x64.msix"
Get-AppxPackage *Qolome*Devtoy* | Remove-AppxPackage
```

或在 **设置 → 应用** 中卸载「Qolome Devtoy」。

## Inno Setup

需安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。脚本按以下顺序查找 `ISCC.exe`：

1. 环境变量 `ISCC`（完整路径）或 `INNO_SETUP_HOME`（安装目录）
2. `PATH` 中的 `ISCC.exe`
3. 常见目录（如 `Program Files\Inno Setup 6`）
4. 卸载项注册表（`Inno Setup 6_is1` → `InstallLocation`）

手动编译：

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.0 packaging\inno\Qolome.Devtoy.iss
```

## 代码签名（可选）

1. 参照 [`config/signing.example.properties`](../config/signing.example.properties) 准备本地 `signing.properties`（勿提交仓库）。
2. 实现 [`scripts/signing_env.ps1`](../scripts/signing_env.ps1)（加载属性并暴露 `Get-WindowsCodeSignPfx` 等函数），或设置 `QOLOME_SIGNING_PROPERTIES`。
3. 打包时若 PFX 可用，将自动：
   - **MSIX**：`AppxPackageSigningEnabled` + 校验证书 Subject
   - **便携 exe / Inno Setup**：`signtool` Authenticode
   - **便携 ZIP**：`.sha256` + CMS `.p7s` 旁路摘要

`Package.appxmanifest` 中 `Identity@Publisher` 须与 PFX 证书 Publisher 一致。

未配置 PFX 时仍会产出**未签名**包并给出警告；正式发布请使用 `-RequireSigning`。

## 说明

- Release publish：**自包含 x64**，MSIX 构建时 `PublishTrimmed=false`（避免 WinUI 裁剪问题）。
- 便携版与 Inno 安装目录均含完整运行时，不依赖预装 Windows App SDK。