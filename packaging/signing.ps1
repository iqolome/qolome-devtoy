# 复用 qolome_epsims_app 统一签名（signing.properties + JfondaCodeSign.pfx）
# 用法: . .\packaging\signing.ps1

$ErrorActionPreference = "Stop"

$PackagingDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent $PackagingDir
$WorkspaceRoot = Split-Path -Parent $RepoRoot
$EpsimsSigningEnv = Join-Path $WorkspaceRoot "qolome_epsims_app\scripts\signing_env.ps1"

if (-not (Test-Path $EpsimsSigningEnv)) {
    throw @"
未找到统一签名脚本: $EpsimsSigningEnv
请确保兄弟仓 qolome_epsims_app 存在，或设置 QOLOME_SIGNING_PROPERTIES 后在本仓复制 scripts/signing_env.ps1。
"@
}

. $EpsimsSigningEnv

# 与 Package.appxmanifest / pubspec msix_config.publisher 一致
$script:DevtoyExpectedPublisher = $script:ExpectedWindowsPublisher

function Initialize-DevtoySigning {
    param(
        [switch]$UseTestCertificate,
        [switch]$RequireSigning
    )

    Set-SigningReleaseEnabled -Enabled $true
    if ($UseTestCertificate.IsPresent) {
        if ($env:QOLOME_ALLOW_TEST_CERT -ne '1') {
            throw "拒绝测试证书：设 QOLOME_ALLOW_TEST_CERT=1 且传 -UseTestCertificate（仅本地调试）。"
        }
        Set-AllowMsixTestCertificate -Allow $true
    } else {
        Set-AllowMsixTestCertificate -Allow $false
    }

    if ($RequireSigning) {
        Assert-UnifiedSigningReady -ProjectRoot $RepoRoot -Platform windows
        return $true
    }

    if (Test-MsixSigningConfigured) {
        try {
            Assert-UnifiedSigningReady -ProjectRoot $RepoRoot -Platform windows
            return $true
        } catch {
            Write-Warning $_.Exception.Message
            return $false
        }
    }

    Write-Warning "未配置企业 PFX（$($script:DefaultSigningProperties)），将产出未签名包。"
    return $false
}

function Get-DevtoyMsixPublishSigningArgs {
  $pfx = Get-WindowsCodeSignPfx
  $tsa = if ($env:QOLOME_TIMESTAMP_URL) { $env:QOLOME_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
  $args = @(
      "-p:AppxPackageSigningEnabled=true",
      "-p:PackageCertificateKeyFile=$($pfx.Path)",
      # WinAppSDK 部分版本会忽略这两项；package.ps1 仍会在产出后用 SignTool /tr 补时间戳
      "-p:AppxPackageSigningTimestampServerUrl=$tsa",
      "-p:AppxPackageSigningTimestampDigestAlgorithm=SHA256"
  )
  if (-not [string]::IsNullOrEmpty($pfx.Password)) {
      $args += "-p:PackageCertificatePassword=$($pfx.Password)"
  }
  return $args
}