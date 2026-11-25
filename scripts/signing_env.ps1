# DevToy Windows 代码签名（本地 properties 或 GitHub Actions Secrets）
# 用法: . .\scripts\signing_env.ps1

$ErrorActionPreference = "Stop"

if (-not (Get-Command Get-FileSha256Hex -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot "file_hash.ps1")
}

$script:DefaultSigningProperties = $null
$script:SigningPropertiesPath = $null
$script:SigningProperties = @{}
$script:SigningReleaseEnabled = $true
$script:AllowMsixTestCertificate = $false
$script:ExpectedWindowsPublisher = "CN=Qolome, O=Qolome, C=CN"
$script:ForbiddenMsixSubjects = @("CN=Msix Testing", "Msix Testing Corporation")

if (-not [string]::IsNullOrWhiteSpace($env:QOLOME_EXPECTED_WINDOWS_PUBLISHER)) {
    $script:ExpectedWindowsPublisher = $env:QOLOME_EXPECTED_WINDOWS_PUBLISHER.Trim()
}

function Set-SigningReleaseEnabled {
    param([bool]$Enabled)
    $script:SigningReleaseEnabled = $Enabled
}

function Set-AllowMsixTestCertificate {
    param([bool]$Allow)
    $script:AllowMsixTestCertificate = $Allow
}

function Read-SigningPropertiesFile {
    param([string]$Path)

    $props = @{}
    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trimmed.Substring(0, $eq).Trim()
        $value = $trimmed.Substring($eq + 1).Trim()
        $props[$key] = $value
    }
    return $props
}

function Read-SigningSecret {
    param([string]$Value, [string]$SidecarPath)

    if (-not [string]::IsNullOrWhiteSpace($Value)) { return $Value.Trim() }
    if ($SidecarPath -and (Test-Path $SidecarPath)) {
        return (Get-Content -Path $SidecarPath -Raw -Encoding UTF8).Trim()
    }
    return $null
}

function Initialize-CiSigningFromGitHubSecrets {
    $b64 = $env:WINDOWS_CODE_SIGN_PFX_BASE64
    if ([string]::IsNullOrWhiteSpace($b64)) { return $false }

    $root = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
    $dir = Join-Path $root "devtoy-signing"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    $pfxPath = Join-Path $dir "codesign.pfx"
    $bytes = [Convert]::FromBase64String(($b64 -replace '\s', ''))
    [System.IO.File]::WriteAllBytes($pfxPath, $bytes)

    $password = $env:WINDOWS_CODE_SIGN_PFX_PASSWORD
    if ($null -eq $password) { $password = '' }

    $script:SigningPropertiesPath = $null
    $script:SigningProperties = @{
        'windows.certificateFile'         = $pfxPath
        'windows.msixCertificateFile'     = $pfxPath
        'windows.certificatePassword'     = $password
        'windows.msixCertificatePassword' = $password
    }
    Write-Host "==> Signing: loaded PFX from GitHub Actions secret (CI)" -ForegroundColor Cyan
    return $true
}

function Get-SigningPropertiesPath {
    if ($script:SigningPropertiesPath) { return $script:SigningPropertiesPath }

    $candidates = @()
    if ($env:QOLOME_SIGNING_PROPERTIES) { $candidates += $env:QOLOME_SIGNING_PROPERTIES }
    if ($script:DefaultSigningProperties) { $candidates += $script:DefaultSigningProperties }

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) {
            $script:SigningPropertiesPath = (Resolve-Path $path).Path
            return $script:SigningPropertiesPath
        }
    }
    return $null
}

function Initialize-SigningConfig {
    if (Initialize-CiSigningFromGitHubSecrets) { return $true }

    $path = Get-SigningPropertiesPath
    if (-not $path) {
        $script:SigningProperties = @{}
        return $false
    }

    $script:SigningPropertiesPath = $path
    $script:SigningProperties = Read-SigningPropertiesFile -Path $path
    return $true
}

function Get-ResolvedMsixCertificateFile {
    if (-not $script:SigningProperties) { return $null }
    $file = $script:SigningProperties['windows.msixCertificateFile']
    if ([string]::IsNullOrWhiteSpace($file)) {
        $file = $script:SigningProperties['windows.certificateFile']
    }
    return $file
}

function Get-MsixCertificatePassword {
    param([hashtable]$Props)

    $certFile = Get-ResolvedMsixCertificateFile
    $sidecar = if ($Props['windows.certificatePasswordFile']) {
        $Props['windows.certificatePasswordFile']
    } elseif ($certFile) {
        "$certFile.pwd"
    } else {
        $null
    }

    $fromMsix = Read-SigningSecret -Value $Props['windows.msixCertificatePassword'] -SidecarPath $null
    if (-not [string]::IsNullOrWhiteSpace($fromMsix)) { return $fromMsix }
    $fromWin = Read-SigningSecret -Value $Props['windows.certificatePassword'] -SidecarPath $sidecar
    if ($null -ne $fromWin) { return $fromWin }
    return ''
}

function Test-MsixSigningConfigured {
    $certFile = Get-ResolvedMsixCertificateFile
    if ([string]::IsNullOrWhiteSpace($certFile)) { return $false }
    return Test-Path $certFile
}

function Get-WindowsCodeSignPfx {
    $certFile = Get-ResolvedMsixCertificateFile
    if ([string]::IsNullOrWhiteSpace($certFile) -or -not (Test-Path $certFile)) {
        throw "Windows 代码签名 PFX 未找到。CI 请配置 Secrets；本地请配置 QOLOME_SIGNING_PROPERTIES。"
    }
    $password = Get-MsixCertificatePassword -Props $script:SigningProperties
    if ($null -eq $password) { $password = '' }
    return [PSCustomObject]@{
        Path     = (Resolve-Path $certFile).Path
        Password = $password
    }
}

function Assert-UnifiedSigningReady {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [ValidateSet('android', 'windows', 'web', 'all')]
        [string]$Platform = 'all'
    )

    if ($Platform -ne 'windows' -and $Platform -ne 'web' -and $Platform -ne 'all') {
        throw "DevToy 仅支持 Windows 平台签名（收到 Platform=$Platform）。"
    }

    if ($script:AllowMsixTestCertificate) {
        Write-Host "    WARN: 已允许测试证书，跳过企业 PFX 校验" -ForegroundColor Yellow
        return
    }

    if (-not (Test-MsixSigningConfigured)) {
        throw @"
Windows 企业代码签名未就绪。
- GitHub Actions：在仓库 Settings → Secrets 配置 WINDOWS_CODE_SIGN_PFX_BASE64、WINDOWS_CODE_SIGN_PFX_PASSWORD
- 本地：设置 QOLOME_SIGNING_PROPERTIES 指向 signing.properties（见 config/signing.example.properties）
"@
    }

    $pfx = Get-WindowsCodeSignPfx
    try {
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx.Path, $pfx.Password)
    } catch {
        throw "无法打开 Windows 代码签名 PFX: $($pfx.Path) — $($_.Exception.Message)"
    }
    if ($cert.Subject -ne $script:ExpectedWindowsPublisher) {
        throw "PFX Subject 与 Package.appxmanifest Publisher 不一致。`n  PFX: $($cert.Subject)`n  期望: $($script:ExpectedWindowsPublisher)"
    }
}

function Find-SignTool {
    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) { return $null }
    $found = Get-ChildItem -Path $kitsRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($found) { return $found.FullName }
    return $null
}

function Ensure-PowerShellSecurityModule {
    if (Get-Module -Name Microsoft.PowerShell.Security) { return $true }
    try {
        Import-Module Microsoft.PowerShell.Security -Force -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Get-AuthenticodeSignatureSafe {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    if (-not (Ensure-PowerShellSecurityModule)) { return $null }
    try {
        return Get-AuthenticodeSignature -FilePath $FilePath -ErrorAction Stop
    } catch {
        return $null
    }
}

function Assert-WindowsAuthenticodeUsesEnterpriseCertificate {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    if ($script:AllowMsixTestCertificate) { return }
    $sig = Get-AuthenticodeSignatureSafe -FilePath $FilePath
    if ($sig -and $sig.SignerCertificate) {
        $subject = $sig.SignerCertificate.Subject
        foreach ($bad in $script:ForbiddenMsixSubjects) {
            if ($subject -like "*$bad*") { throw "Authenticode 使用了测试证书: $subject — $FilePath" }
        }
        if ($subject -ne $script:ExpectedWindowsPublisher) {
            throw "Authenticode Subject 不符。`n  实际: $subject`n  期望: $($script:ExpectedWindowsPublisher)`n  文件: $FilePath"
        }
        return
    }
    throw "文件无 Authenticode 签名: $FilePath"
}

function Invoke-WindowsCodeSignFile {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    if ($script:AllowMsixTestCertificate) {
        Write-Host "    WARN: 跳过 SignTool（测试证书模式）: $FilePath" -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path $FilePath)) { throw "待签名文件不存在: $FilePath" }

    $signtool = Find-SignTool
    if (-not $signtool) {
        throw "未找到 signtool.exe（GitHub Actions windows-latest 通常已预装 Windows SDK）。"
    }

    $pfx = Get-WindowsCodeSignPfx
    $passArgs = @()
    if (-not [string]::IsNullOrEmpty($pfx.Password)) {
        $passArgs = @('/p', $pfx.Password)
    }

    $timestampUrls = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://timestamp.globalsign.com/tsa/r6advanced1'
    )

    $signed = $false
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        foreach ($tr in $timestampUrls) {
            & $signtool sign /fd SHA256 /f $pfx.Path @passArgs /tr $tr /td SHA256 $FilePath 2>&1 | ForEach-Object { Write-Host $_ }
            if ($LASTEXITCODE -eq 0) { $signed = $true; break }
        }
        if (-not $signed) {
            & $signtool sign /fd SHA256 /f $pfx.Path @passArgs $FilePath 2>&1 | ForEach-Object { Write-Host $_ }
            if ($LASTEXITCODE -ne 0) { throw "SignTool 签名失败 (exit=$LASTEXITCODE): $FilePath" }
        }
    } finally {
        $ErrorActionPreference = $prevEap
    }

    Assert-WindowsAuthenticodeUsesEnterpriseCertificate -FilePath $FilePath
    Write-Host "    SignTool OK: $FilePath" -ForegroundColor Green
}

function Assert-MsixUsesEnterpriseCertificate {
    param([Parameter(Mandatory = $true)][string]$MsixPath)

    if ($script:AllowMsixTestCertificate) { return }
    if (-not (Test-Path $MsixPath)) { throw "MSIX 不存在: $MsixPath" }

    $sig = Get-AuthenticodeSignatureSafe -FilePath $MsixPath
    if ($sig -and $sig.SignerCertificate) {
        $subject = $sig.SignerCertificate.Subject
        foreach ($bad in $script:ForbiddenMsixSubjects) {
            if ($subject -like "*$bad*") { throw "MSIX 使用了测试证书: $subject — $MsixPath" }
        }
        if ($subject -ne $script:ExpectedWindowsPublisher) {
            throw "MSIX 签名 Subject 不符。`n  实际: $subject`n  期望: $($script:ExpectedWindowsPublisher)"
        }
        Write-Host "    MSIX 签名校验通过: $subject" -ForegroundColor Green
        return
    }
    throw "MSIX 未签名或无法读取 Authenticode: $MsixPath"
}

function Find-OpenSsl {
    $candidates = @(
        (Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles}\OpenSSL-Win64\bin\openssl.exe",
        "${env:ProgramFiles(x86)}\OpenSSL-Win32\bin\openssl.exe",
        "C:\Program Files\OpenSSL-Win64\bin\openssl.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Assert-ReleaseArchiveCmsSignature {
    param([Parameter(Mandatory = $true)][string]$ArchivePath)

    $openssl = Find-OpenSsl
    if (-not $openssl) { throw "未找到 openssl.exe" }

    $p7sPath = "$ArchivePath.p7s"
    $shaPath = "$ArchivePath.sha256"
    if (-not (Test-Path $p7sPath)) { throw "缺少 CMS 签名: $p7sPath" }
    if (-not (Test-Path $shaPath)) { throw "缺少 SHA256 摘要: $shaPath" }

    $expected = Get-FileSha256Hex -Path $ArchivePath
    $recorded = ((Get-Content -LiteralPath $shaPath -Raw) -split '\s+')[0].ToLowerInvariant()
    if ($expected -ne $recorded) { throw "SHA256 不匹配: $ArchivePath" }

    $pfx = Get-WindowsCodeSignPfx
    $work = Join-Path $env:TEMP ("cms-verify-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $certPem = Join-Path $work 'cert.pem'
    $passin = if ([string]::IsNullOrEmpty($pfx.Password)) { 'pass:' } else { "pass:$($pfx.Password)" }
    try {
        & $openssl pkcs12 -in $pfx.Path -clcerts -nokeys -out $certPem -passin $passin 2>&1 | Out-Null
        & $openssl cms -verify -binary -inform DER -in $p7sPath -content $ArchivePath `
            -CAfile $certPem -noverify 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "openssl cms -verify 失败" }
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-ReleaseArchiveCmsSign {
    param([Parameter(Mandatory = $true)][string]$ArchivePath)

    if (-not (Test-Path $ArchivePath)) { throw "归档不存在: $ArchivePath" }

    $openssl = Find-OpenSsl
    if (-not $openssl) {
        throw "未找到 openssl.exe。GitHub Actions release 工作流会通过 choco 安装 OpenSSL。"
    }

    $hash = Get-FileSha256Hex -Path $ArchivePath
    $leaf = Split-Path -Leaf $ArchivePath
    $shaPath = "$ArchivePath.sha256"
    Set-Content -LiteralPath $shaPath -Value "$hash  $leaf" -Encoding ascii

    $pfx = Get-WindowsCodeSignPfx
    $p7sPath = "$ArchivePath.p7s"
    $work = Join-Path $env:TEMP ("cms-sign-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $keyPem = Join-Path $work 'key.pem'
    $certPem = Join-Path $work 'cert.pem'
    $passin = if ([string]::IsNullOrEmpty($pfx.Password)) { 'pass:' } else { "pass:$($pfx.Password)" }
    try {
        & $openssl pkcs12 -in $pfx.Path -nocerts -nodes -out $keyPem -passin $passin 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "openssl pkcs12 导出私钥失败" }
        & $openssl pkcs12 -in $pfx.Path -clcerts -nokeys -out $certPem -passin $passin 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "openssl pkcs12 导出证书失败" }
        & $openssl cms -sign -binary -in $ArchivePath -outform DER -out $p7sPath `
            -signer $certPem -inkey $keyPem -md sha256 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $p7sPath)) { throw "openssl cms -sign 失败" }
        Assert-ReleaseArchiveCmsSignature -ArchivePath $ArchivePath
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Write-ReleaseArtifactVerifyFiles {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string]$ReleaseRoot = ''
    )

    if (-not (Test-Path -LiteralPath $FilePath)) { throw "产物不存在: $FilePath" }

    $item = Get-Item -LiteralPath $FilePath
    $sha = Get-FileSha256Hex -Path $item.FullName
    $shaPath = "$($item.FullName).sha256"
    Set-Content -LiteralPath $shaPath -Value "$sha  $($item.Name)" -Encoding ascii

    $ext = [System.IO.Path]::GetExtension($FilePath).ToLowerInvariant()
    switch ($ext) {
        '.msix' {
            Assert-MsixUsesEnterpriseCertificate -MsixPath $FilePath
            $scheme = 'authenticode'
        }
        '.exe' {
            Assert-WindowsAuthenticodeUsesEnterpriseCertificate -FilePath $FilePath
            $scheme = 'authenticode'
        }
        '.zip' {
            if (-not (Test-Path "$FilePath.p7s")) { Invoke-ReleaseArchiveCmsSign -ArchivePath $FilePath }
            else { Assert-ReleaseArchiveCmsSignature -ArchivePath $FilePath }
            $scheme = 'cms+sha256'
        }
        default { $scheme = 'unknown' }
    }

    $pfx = Get-WindowsCodeSignPfx
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx.Path, $pfx.Password)

    $relative = if ($ReleaseRoot -and (Test-Path $ReleaseRoot)) {
        $item.FullName.Substring((Resolve-Path $ReleaseRoot).Path.Length).TrimStart('\', '/')
    } else {
        $item.Name
    }

    $sidecars = @("$($item.Name).sha256", "$($item.Name).siginfo.json")
    if (Test-Path "$($item.FullName).p7s") { $sidecars += "$($item.Name).p7s" }

    $siginfo = [ordered]@{
        file             = $item.Name
        relativePath     = ($relative -replace '\\', '/')
        sha256           = $sha
        sizeBytes        = $item.Length
        signedAt         = (Get-Date).ToUniversalTime().ToString('o')
        scheme           = $scheme
        signerSubject    = $cert.Subject
        signerThumbprint = $cert.Thumbprint
        embeddedVerified = $true
        sidecars         = $sidecars
    }
    $sigPath = "$($item.FullName).siginfo.json"
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($sigPath, ($siginfo | ConvertTo-Json -Depth 6), $utf8NoBom)
    return [PSCustomObject]$siginfo
}

Initialize-SigningConfig | Out-Null