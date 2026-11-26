# Qolome Devtoy 一键打包：便携 ZIP / MSIX / Inno Setup（可选企业代码签名）
# 用法:
#   .\packaging\package.ps1                    # 全部
#   .\packaging\package.ps1 -Target Zip
#   .\packaging\package.ps1 -RequireSigning    # 未配置 PFX 则失败

[CmdletBinding()]
param(
    [ValidateSet("All", "Zip", "Msix", "Inno")]
    [string] $Target = "All",

    [string] $Version,

    [switch] $SkipPublish,

    [switch] $SkipSigning,

    [switch] $RequireSigning,

    # 仅调试：须 QOLOME_ALLOW_TEST_CERT=1
    [switch] $UseTestCertificate
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$AppProj = Join-Path $RepoRoot "src\Qolome.Devtoy.App\Qolome.Devtoy.App.csproj"
$DistRoot = Join-Path $RepoRoot "dist"
$ReleaseDir = Join-Path $DistRoot "release"
$PublishDir = Join-Path $DistRoot "publish\win-x64"
$SetupDir = $ReleaseDir
$MsixDir = $ReleaseDir

. (Join-Path $PSScriptRoot "signing.ps1")

$SigningReady = $false
if (-not $SkipSigning) {
    $SigningReady = Initialize-DevtoySigning -UseTestCertificate:$UseTestCertificate -RequireSigning:$RequireSigning
} elseif ($RequireSigning) {
    throw "-SkipSigning 与 -RequireSigning 不能同时使用"
}

if (-not $Version) {
    [xml] $proj = Get-Content $AppProj
    $Version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if (-not $Version) { $Version = "1.1.0" }
}

function Get-MsixFourPartVersion([string] $ver) {
    $parts = $ver.Split('.')
    while ($parts.Count -lt 4) { $parts += '0' }
    if ($parts.Count -gt 4) { $parts = $parts[0..3] }
    return ($parts -join '.')
}

$MsixVersion = Get-MsixFourPartVersion $Version

function Write-Step([string] $msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Find-InnoSetupCompiler {
    # 优先显式覆盖：ISCC=完整路径，或 INNO_SETUP_HOME=安装目录
    foreach ($envPath in @($env:ISCC, $(if ($env:INNO_SETUP_HOME) { Join-Path $env:INNO_SETUP_HOME "ISCC.exe" }))) {
        if ($envPath -and (Test-Path -LiteralPath $envPath)) {
            return (Resolve-Path -LiteralPath $envPath).Path
        }
    }

    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "D:\Software\Inno Setup 6\ISCC.exe",
        "C:\Software\Inno Setup 6\ISCC.exe"
    )

    $uninstallKeys = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
    )
    foreach ($key in $uninstallKeys) {
        if (-not (Test-Path -LiteralPath $key)) { continue }
        $props = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        $root = $props.InstallLocation
        if (-not $root) { $root = $props.'Inno Setup: App Path' }
        if ($root) {
            $candidates += (Join-Path $root.TrimEnd('\') "ISCC.exe")
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Find-MsPdbCmfExe {
    # 仅接受 MSVC Host / AppxPackage 中的工具；ScopeCppSDK\vc15 过旧，会以 1106 失败
    $vsRoots = @()
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -products * -property installationPath 2>$null
        if ($found) { $vsRoots += @($found) }
    }
    $vsRoots += @(
        "D:\Software\MicrosoftVisualStudio\2026\Community",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community"
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($root in ($vsRoots | Select-Object -Unique)) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) { continue }
        $msvcRoot = Join-Path $root "VC\Tools\MSVC"
        if (Test-Path -LiteralPath $msvcRoot) {
            Get-ChildItem -LiteralPath $msvcRoot -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object {
                    $candidates.Add((Join-Path $_.FullName "bin\Hostx64\x64\mspdbcmf.exe"))
                    $candidates.Add((Join-Path $_.FullName "bin\HostX64\x64\mspdbcmf.exe"))
                }
        }
        foreach ($ver in @("v18.0", "v17.0", "v16.0")) {
            $candidates.Add((Join-Path $root "MSBuild\Microsoft\VisualStudio\$ver\AppxPackage\x64\mspdbcmf.exe"))
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Sign-PublishArtifacts {
    if (-not $SigningReady) { return }
    $mainExe = Join-Path $PublishDir "Qolome.Devtoy.App.exe"
    if (Test-Path $mainExe) {
        Write-Step "Authenticode 签名主程序"
        Invoke-WindowsCodeSignFile -FilePath $mainExe
    }
}

function Ensure-Publish {
    if ($SkipPublish) {
        if (-not (Test-Path (Join-Path $PublishDir "Qolome.Devtoy.App.exe"))) {
            throw "未找到已发布目录: $PublishDir（去掉 -SkipPublish 或先执行 publish）"
        }
        Sign-PublishArtifacts
        return
    }

    Write-Step "dotnet publish (Release, win-x64, 自包含)"
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
    dotnet publish $AppProj `
        -c Release `
        -r win-x64 `
        -p:Platform=x64 `
        -p:PublishProfile=win-x64-release `
        -p:PublishDir="$PublishDir\" `
        -p:Version=$Version `
        -p:WinAppRunDevTools=off
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }
    Sign-PublishArtifacts
}

function New-PortableZip {
    Write-Step "便携 ZIP"
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
    $zipName = "Qolome.Devtoy_$Version`_win-x64_portable.zip"
    $zipPath = Join-Path $ReleaseDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    $readme = @"
Qolome Devtoy $Version — Windows 便携版 (x64)

1. 解压到任意目录（路径勿含特殊字符为佳）
2. 运行 Qolome.Devtoy.App.exe
3. 配置与缓存: %LocalAppData%\Qolome\Devtoy\

无需安装；删除文件夹即可卸载。
"@
    $readmePath = Join-Path $PublishDir "README-portable.txt"
    Set-Content -Path $readmePath -Value $readme -Encoding UTF8

    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Remove-Item $readmePath -Force -ErrorAction SilentlyContinue
    Write-Host "输出: $zipPath"
    if ($SigningReady) {
        Invoke-ReleaseArchiveCmsSign -ArchivePath $zipPath
        Write-ReleaseArtifactVerifyFiles -FilePath $zipPath -ReleaseRoot $ReleaseDir | Out-Null
    }
}

function New-MsixPackage {
    Write-Step "MSIX 安装包"
    New-Item -ItemType Directory -Force -Path $MsixDir | Out-Null

    $pub = @(
        "publish", $AppProj,
        "-c", "Release",
        "-r", "win-x64",
        "-p:Platform=x64",
        "-p:GenerateAppxPackageOnBuild=true",
        "-p:UapAppxPackageBuildMode=SideloadOnly",
        "-p:AppxBundle=Never",
        "-p:PublishTrimmed=false",
        "-p:Version=$Version",
        "-p:AppxPackageVersion=$MsixVersion",
        "-p:WinAppRunDevTools=off"
    )
    if ($SigningReady) {
        $pub += Get-DevtoyMsixPublishSigningArgs
    } else {
        $pub += "-p:AppxPackageSigningEnabled=false"
    }

    $mspdbcmf = Find-MsPdbCmfExe
    if ($mspdbcmf) {
        Write-Host "mspdbcmf: $mspdbcmf"
        $pub += "-p:MsPdbCmfExeFullpath=$mspdbcmf"
    } else {
        # VS2026 的 ScopeCppSDK\vc15\mspdbcmf 过旧不可用；清空 PDBPayload 以消除警告并跳过 .appxsym
        Write-Host "mspdbcmf: 未找到兼容版本，已跳过 Appx 符号包（不影响 .msix 安装）"
        $pub += "-p:DevtoySkipAppxSymbols=true"
    }

    & dotnet @pub
    $publishExit = $LASTEXITCODE

    $searchRoots = @(
        (Join-Path $RepoRoot "src\Qolome.Devtoy.App\AppPackages"),
        (Join-Path $RepoRoot "src\Qolome.Devtoy.App\bin\x64\Release")
    )
    $msixFiles = foreach ($root in $searchRoots) {
        if (Test-Path $root) {
            Get-ChildItem -Path $root -Filter *.msix -Recurse -ErrorAction SilentlyContinue
        }
    }

    if (-not $msixFiles) {
        if ($publishExit -ne 0) { throw "MSIX 生成失败（且无 .msix 产出）" }
        Write-Warning "未在 AppPackages 下找到 .msix，请检查 Windows App SDK / 打包工具。"
        return
    }

    if ($publishExit -ne 0) {
        Write-Warning "publish 退出码 $publishExit，但已找到 MSIX，将继续复制到 dist\release。"
    }

    $latest = $msixFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $dest = Join-Path $ReleaseDir "Qolome.Devtoy_$Version`_x64.msix"
    Copy-Item $latest.FullName $dest -Force

    $bundle = Get-ChildItem -Path $latest.DirectoryName -Filter *.msixbundle -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($bundle) {
        Copy-Item $bundle.FullName (Join-Path $ReleaseDir "Qolome.Devtoy_$Version`_x64.msixbundle") -Force
    }

    if ($SigningReady) {
        # MSBuild 侧签常无时间戳（属性显示「不可用」）；与 Inno 一样用 SignTool /tr 重签
        Write-Step "MSIX Authenticode 时间戳"
        Invoke-WindowsCodeSignFile -FilePath $dest
        Assert-MsixUsesEnterpriseCertificate -MsixPath $dest
        Write-ReleaseArtifactVerifyFiles -FilePath $dest -ReleaseRoot $ReleaseDir | Out-Null
    }

    Write-Host "输出: $dest"
    Write-Host "安装: Add-AppxPackage -Path '$dest'"
    Write-Host "卸载: Get-AppxPackage *Devtoy* | Remove-AppxPackage  (或在「设置 → 应用」中卸载)"
}

function New-InnoSetup {
    Write-Step "Inno Setup (.exe)"
    if (-not (Test-Path (Join-Path $PublishDir "Qolome.Devtoy.App.exe"))) {
        throw "请先完成 publish（便携目录不存在）"
    }

    $iscc = Find-InnoSetupCompiler
    if (-not $iscc) {
        Write-Warning "未找到 Inno Setup 6 编译器 ISCC.exe。请安装后重试，或设置环境变量 ISCC / INNO_SETUP_HOME。"
        Write-Host "手动: `"D:\Software\Inno Setup 6\ISCC.exe`" /DMyAppVersion=$Version packaging\inno\Qolome.Devtoy.iss"
        return
    }

    Write-Host "ISCC: $iscc"

    New-Item -ItemType Directory -Force -Path $SetupDir | Out-Null
    $iss = Join-Path $RepoRoot "packaging\inno\Qolome.Devtoy.iss"
    $sourceAbs = (Resolve-Path $PublishDir).Path
    $outputAbs = (Resolve-Path $SetupDir).Path
    $iconAbs = (Resolve-Path (Join-Path $RepoRoot "icons\app.ico")).Path

    & $iscc `
        "/DMyAppVersion=$Version" `
        "/DSourceDir=$sourceAbs" `
        "/DOutputDirPath=$outputAbs" `
        "/DSetupIconPath=$iconAbs" `
        $iss

    if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败" }

    Get-ChildItem $SetupDir -Filter "Qolome.Devtoy_*_Setup.exe" | ForEach-Object {
        if ($SigningReady) {
            Invoke-WindowsCodeSignFile -FilePath $_.FullName
            Write-ReleaseArtifactVerifyFiles -FilePath $_.FullName -ReleaseRoot $ReleaseDir | Out-Null
        }
        Write-Host "输出: $($_.FullName)"
    }
}

Write-Host "Qolome Devtoy 打包 v$Version (MSIX $MsixVersion)  Target=$Target  Signing=$SigningReady"
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

switch ($Target) {
    "All" {
        Ensure-Publish
        New-PortableZip
        New-MsixPackage
        New-InnoSetup
    }
    "Zip" {
        Ensure-Publish
        New-PortableZip
    }
    "Msix" { New-MsixPackage }
    "Inno" {
        Ensure-Publish
        New-InnoSetup
    }
}

Write-Step "完成。产物目录: $ReleaseDir"