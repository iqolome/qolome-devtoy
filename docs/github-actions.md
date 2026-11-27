# GitHub Actions 构建与签名

GitHub 提供免费的 `windows-latest` 托管 Runner（公开仓分钟额度较高），本仓用它完成 **编译** 与 **企业代码签名打包**。证书仍由团队自备，仅以 Secret 形式注入 CI，**不会**写入 Git。

## 工作流

### CI（`.github/workflows/ci.yml`）

- 触发：`develop` / `main` / `master` / `github` 推送，或对上述分支的 PR
- 步骤：`dotnet build` + `package.ps1 -SkipSigning -Target Zip`

### Release（`.github/workflows/release.yml`）

- 触发：推送标签 `v*`（如 `v1.1.2`），或仓库 **Actions → Release → Run workflow**
- 安装：Inno Setup、OpenSSL（便携 ZIP 的 CMS 签名）
- 执行：`packaging/package.ps1 -RequireSigning`
- 产出：上传 `dist/release/*` 为 Artifact；打 tag 时另建 GitHub Release

## 配置 Secrets

1. 将企业 PFX 转为 Base64（PowerShell 示例，勿在聊天中粘贴结果）：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('path\to\codesign.pfx')) | Set-Clipboard
```

2. 在 GitHub 仓库添加 Secrets：`WINDOWS_CODE_SIGN_PFX_BASE64`、`WINDOWS_CODE_SIGN_PFX_PASSWORD`

3. 若证书 Subject 与默认 Publisher 不同，设置 `QOLOME_EXPECTED_WINDOWS_PUBLISHER`

## 发版流程

```powershell
.\scripts\bump-version.ps1 -Bump minor   # 或 -Version x.y.z
git add -A
git commit -m "release: v1.1.2"
git tag v1.1.2
git push github HEAD:github
git push github v1.1.2
git push origin HEAD:develop             # GitLab；若历史分叉需协商合并后再推
git push origin v1.1.2
```

在 Actions 页等待 **Release** 完成，从 Release 或 Artifacts 下载 ZIP / MSIX / Setup。

## 本地对照

| 场景 | 命令 |
| --- | --- |
| 日常调试包 | `.\packaging\package.ps1 -SkipSigning` |
| 本地验签（可选） | `$env:QOLOME_SIGNING_PROPERTIES='D:\secure\signing.properties'` 后 `-RequireSigning` |