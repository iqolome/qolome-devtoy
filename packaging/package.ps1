# Qolome Devtoy 一键打包：便携 ZIP / MSIX / Inno Setup（企业代码签名对齐 qolome_epsims_app）
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
$PublishDir = Join-Path $DistRoot "publish\win-x64"
$SetupDir = Join-Path $DistRoot "setup"
$MsixDir = Join-Path $DistRoot "msix"

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
    if (-not $Version) { $Version = "1.0.0" }
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
    $zipName = "Qolome.Devtoy_$Version`_win-x64_portable.zip"
    $zipPath = Join-Path $DistRoot $zipName
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
        Write-ReleaseArtifactVerifyFiles -FilePath $zipPath -ReleaseRoot $DistRoot | Out-Null
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
        Write-Warning "publish 退出码 $publishExit，但已找到 MSIX，将继续复制到 dist。"
    }

    $latest = $msixFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $dest = Join-Path $MsixDir "Qolome.Devtoy_$Version`_x64.msix"
    Copy-Item $latest.FullName $dest -Force

    $bundle = Get-ChildItem -Path $latest.DirectoryName -Filter *.msixbundle -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($bundle) {
        Copy-Item $bundle.FullName (Join-Path $MsixDir "Qolome.Devtoy_$Version`_x64.msixbundle") -Force
    }

    if ($SigningReady) {
        Assert-MsixUsesEnterpriseCertificate -MsixPath $dest
        Write-ReleaseArtifactVerifyFiles -FilePath $dest -ReleaseRoot $DistRoot | Out-Null
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

    $iscc = $null
    foreach ($candidate in @(
            "ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
        )) {
        if ($candidate -eq "ISCC.exe") {
            $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($found) { $iscc = $found.Source; break }
        }
        elseif (Test-Path $candidate) {
            $iscc = $candidate
            break
        }
    }

    if (-not $iscc) {
        Write-Warning "未找到 Inno Setup 编译器 ISCC.exe。请安装 Inno Setup 6 后重试。"
        Write-Host "手动: ISCC.exe /DMyAppVersion=$Version packaging\inno\Qolome.Devtoy.iss"
        return
    }

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
            Write-ReleaseArtifactVerifyFiles -FilePath $_.FullName -ReleaseRoot $DistRoot | Out-Null
        }
        Write-Host "输出: $($_.FullName)"
    }
}

Write-Host "Qolome Devtoy 打包 v$Version (MSIX $MsixVersion)  Target=$Target  Signing=$SigningReady"
New-Item -ItemType Directory -Force -Path $DistRoot | Out-Null

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

Write-Step "完成。产物目录: $DistRoot"