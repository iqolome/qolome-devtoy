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

## 代码签名（GitHub Actions）

**所有带企业签名的发布包**应在 GitHub 上构建，勿把 PFX 提交到仓库。

| 工作流 | 文件 | 说明 |
| --- | --- | --- |
| CI | [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | PR / 推送：`dotnet build` + `-SkipSigning` 便携 ZIP |
| Release | [`.github/workflows/release.yml`](../.github/workflows/release.yml) | 标签 `v*` 或手动触发：`-RequireSigning` 全量产物 |

在仓库 **Settings → Secrets and variables → Actions** 配置：

| Secret | 说明 |
| --- | --- |
| `WINDOWS_CODE_SIGN_PFX_BASE64` | 企业 PFX 的 Base64（单行） |
| `WINDOWS_CODE_SIGN_PFX_PASSWORD` | PFX 口令（可为空字符串） |
| `QOLOME_EXPECTED_WINDOWS_PUBLISHER` | 可选；默认与 `Package.appxmanifest` 的 Publisher 一致 |

签名逻辑见 [`scripts/signing_env.ps1`](../scripts/signing_env.ps1)（CI 从 Secret 解出临时 PFX；本地可用 `QOLOME_SIGNING_PROPERTIES` 指向 `signing.properties`）。

发布包将包含：

- **MSIX**：`AppxPackageSigningEnabled` + Authenticode 时间戳
- **便携 exe / Setup.exe**：`signtool`
- **便携 ZIP**：`.sha256` + CMS `.p7s` 旁路摘要

本地开发默认：

```powershell
.\packaging\package.ps1 -SkipSigning
```

## 说明

- Release publish：**自包含 x64**，MSIX 构建时 `PublishTrimmed=false`（避免 WinUI 裁剪问题）。
- 便携版与 Inno 安装目录均含完整运行时，不依赖预装 Windows App SDK。