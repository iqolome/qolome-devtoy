# 同步应用版本号（GitHub / GitLab 同一脚本；缺文件自动跳过）
# 不含 NuGet PackageReference。以 App csproj 的 <Version> 为当前版本源。
#
# 用法（仓库根目录，develop / github / release 任一分支均可）:
#   .\scripts\bump-version.ps1 -Bump patch
#   .\scripts\bump-version.ps1 -Version 1.2.0
#   .\scripts\bump-version.ps1 -Bump minor -DryRun

[CmdletBinding(DefaultParameterSetName = 'Bump')]
param(
    [Parameter(ParameterSetName = 'Explicit', Mandatory = $true)]
    [string] $Version,

    [Parameter(ParameterSetName = 'Bump')]
    [ValidateSet('major', 'minor', 'patch')]
    [string] $Bump = 'patch',

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$AppCsprojRel = 'src\Qolome.Devtoy.App\Qolome.Devtoy.App.csproj'
$AppCsproj = Join-Path $RepoRoot $AppCsprojRel

function Get-AppVersionFromCsproj([string] $Path) {
    [xml] $xml = Get-Content -LiteralPath $Path -Encoding UTF8
    $ver = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if ([string]::IsNullOrWhiteSpace($ver)) {
        throw "未在 $Path 找到 <Version>"
    }
    return $ver.Trim()
}

function Test-SemVer3([string] $ver) {
    return $ver -match '^\d+\.\d+\.\d+$'
}

function Get-MsixFourPart([string] $ver) {
    $parts = @($ver.Split('.'))
    while ($parts.Count -lt 4) { $parts += '0' }
    $parts = $parts[0..3]
    foreach ($p in $parts) {
        $n = [int]$p
        if ($n -lt 0 -or $n -gt 65535) {
            throw "MSIX 版本段越界 (0..65535): $ver"
        }
    }
    return ($parts -join '.')
}

function Step-SemVer([string] $current, [string] $kind) {
    if (-not (Test-SemVer3 $current)) {
        throw "当前版本不是 x.y.z: $current"
    }
    $a, $b, $c = ($current -split '\.') | ForEach-Object { [int]$_ }
    switch ($kind) {
        'major' { return "{0}.0.0" -f ($a + 1) }
        'minor' { return "{0}.{1}.0" -f $a, ($b + 1) }
        'patch' { return "{0}.{1}.{2}" -f $a, $b, ($c + 1) }
    }
}

function Get-FileUtf8Encoding([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    return [System.Text.UTF8Encoding]::new($hasBom)
}

function Update-File {
    param(
        [Parameter(Mandatory = $true)][string] $RelativePath,
        [Parameter(Mandatory = $true)][scriptblock] $Transform,
        [switch] $Required
    )

    $full = Join-Path $RepoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $full)) {
        if ($Required) {
            throw "缺少必需文件: $RelativePath"
        }
        Write-Host "  - $RelativePath（本分支无此文件，跳过）" -ForegroundColor DarkGray
        return $false
    }

    $encoding = Get-FileUtf8Encoding $full
    $before = [System.IO.File]::ReadAllText($full, $encoding)
    $after = & $Transform $before
    if ($after -eq $before) {
        Write-Host "  = $RelativePath（无变化）" -ForegroundColor DarkGray
        return $false
    }

    if ($DryRun) {
        Write-Host "  ~ $RelativePath（DryRun）" -ForegroundColor Yellow
    } else {
        [System.IO.File]::WriteAllText($full, $after, $encoding)
        Write-Host "  * $RelativePath" -ForegroundColor Green
    }
    return $true
}

function Update-ReadmeVersions([string] $text, [string] $newVer) {
    # shields.io 徽章（GitHub 公开 README、GitLab 内网 README 共用）
    $text = [regex]::Replace($text, 'badge/version-\d+\.\d+\.\d+-', "badge/version-$newVer-")
    $text = [regex]::Replace($text, 'version-\d+\.\d+\.\d+-blue', "version-$newVer-blue")
    $text = [regex]::Replace($text, '当前 `\d+\.\d+\.\d+`', "当前 ``$newVer``")
    $text = [regex]::Replace($text, '(-Version )\d+\.\d+\.\d+', "`${1}$newVer")
    return $text
}

if (-not (Test-Path -LiteralPath $AppCsproj)) {
    throw "未找到 $AppCsprojRel"
}

$OldVersion = Get-AppVersionFromCsproj $AppCsproj
if ($PSCmdlet.ParameterSetName -eq 'Explicit') {
    $NewVersion = $Version.Trim()
    if ($NewVersion.StartsWith('v') -or $NewVersion.StartsWith('V')) {
        $NewVersion = $NewVersion.Substring(1)
    }
} else {
    $NewVersion = Step-SemVer -current $OldVersion -kind $Bump
}

if (-not (Test-SemVer3 $NewVersion)) {
    throw "目标版本必须是 x.y.z（不要带 v 前缀）: $NewVersion"
}
if ($NewVersion -eq $OldVersion) {
    Write-Warning "目标版本与当前相同 ($NewVersion)，仍会扫描并对齐各文件。"
}

$OldMsix = Get-MsixFourPart $OldVersion
$NewMsix = Get-MsixFourPart $NewVersion
$branch = (git -C $RepoRoot branch --show-current 2>$null)
if (-not $branch) { $branch = '(unknown)' }

Write-Host ""
Write-Host "==> 分支: $branch" -ForegroundColor Cyan
Write-Host "==> 版本: $OldVersion → $NewVersion  (MSIX $OldMsix → $NewMsix)" -ForegroundColor Cyan
if ($DryRun) { Write-Host "    DryRun：只预览，不写盘" -ForegroundColor Yellow }

# --- 共有：src 下所有带 <Version> 元素的 csproj ---
$csprojFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Filter '*.csproj' -Recurse -File |
    Where-Object {
        $c = [System.IO.File]::ReadAllText($_.FullName)
        $c -match '(?m)^\s*<Version>[^<]+</Version>\s*$'
    }
foreach ($f in $csprojFiles) {
    $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    Update-File -RelativePath $rel -Required -Transform {
        param($t)
        [regex]::Replace($t, '(?m)(^\s*<Version>)[^<]+(</Version>\s*$)', "`${1}$NewVersion`${2}")
    } | Out-Null
}

Update-File -RelativePath 'src\Qolome.Devtoy.App\Package.appxmanifest' -Required -Transform {
    param($t)
    [regex]::Replace($t, '(<Identity\b[^>]*\bVersion=")[^"]+(")', "`${1}$NewMsix`${2}")
} | Out-Null

Update-File -RelativePath 'sonar-project.properties' -Transform {
    param($t)
    if ($t -match '(?m)^sonar\.projectVersion\s*=') {
        return [regex]::Replace($t, '(?m)^sonar\.projectVersion\s*=.*$', "sonar.projectVersion=$NewVersion")
    }
    return ($t.TrimEnd() + "`nsonar.projectVersion=$NewVersion`n")
} | Out-Null

Update-File -RelativePath 'README.md' -Required -Transform {
    param($t)
    Update-ReadmeVersions -text $t -newVer $NewVersion
} | Out-Null

Update-File -RelativePath 'src\Qolome.Devtoy.Core\AboutInfo.cs' -Transform {
    param($t)
    [regex]::Replace($t, '(\?\?\s*")\d+\.\d+\.\d+(")', "`${1}$NewVersion`${2}")
} | Out-Null

Update-File -RelativePath 'packaging\package.ps1' -Transform {
    param($t)
    [regex]::Replace($t, '(\$Version\s*=\s*")\d+\.\d+\.\d+(")', "`${1}$NewVersion`${2}")
} | Out-Null

Update-File -RelativePath 'packaging\inno\Qolome.Devtoy.iss' -Transform {
    param($t)
    $t = [regex]::Replace($t, '(#define MyAppVersion ")[^"]+(")', "`${1}$NewVersion`${2}")
    $t = [regex]::Replace($t, '(/DMyAppVersion=)\d+\.\d+\.\d+', "`${1}$NewVersion")
    $t
} | Out-Null

Update-File -RelativePath 'packaging\README.md' -Transform {
    param($t)
    $t = [regex]::Replace($t, 'Qolome\.Devtoy_\d+\.\d+\.\d+_x64\.msix', "Qolome.Devtoy_${NewVersion}_x64.msix")
    $t = [regex]::Replace($t, 'Qolome\.Devtoy_\d+\.\d+\.\d+_win-x64_portable\.zip', "Qolome.Devtoy_${NewVersion}_win-x64_portable.zip")
    $t = [regex]::Replace($t, 'Qolome\.Devtoy_\d+\.\d+\.\d+_Setup\.exe', "Qolome.Devtoy_${NewVersion}_Setup.exe")
    $t = [regex]::Replace($t, '(/DMyAppVersion=)\d+\.\d+\.\d+', "`${1}$NewVersion")
    $t = [regex]::Replace($t, '(-Version )\d+\.\d+\.\d+', "`${1}$NewVersion")
    $t
} | Out-Null

# --- 主机相关文档（有则更新）---
Update-File -RelativePath 'docs\github-actions.md' -Transform {
    param($t)
    $t = [regex]::Replace($t, '(git tag v)\d+\.\d+\.\d+', "`${1}$NewVersion")
    $t = [regex]::Replace($t, '(?<![\w.])v\d+\.\d+\.\d+(?![\w.])', "v$NewVersion")
    $t
} | Out-Null

Update-File -RelativePath 'docs\gitlab-ci.md' -Transform {
    param($t)
    $t = [regex]::Replace($t, '(?<![\w.])v?\d+\.\d+\.\d+(?=[\s"`''\)|/]|$)', {
        param($m)
        $s = $m.Value
        if ($s.StartsWith('v')) { "v$NewVersion" } else { $NewVersion }
    })
    $t
} | Out-Null

Update-File -RelativePath '.gitlab-ci.yml' -Transform {
    param($t)
    # 仅当流水线里写死了应用版本字面量时才替换（常见变量名）
    $t = [regex]::Replace($t, '(APP_VERSION:\s*["'']?)\d+\.\d+\.\d+', "`${1}$NewVersion")
    $t = [regex]::Replace($t, '(DEVTOY_VERSION:\s*["'']?)\d+\.\d+\.\d+', "`${1}$NewVersion")
    $t
} | Out-Null

Write-Host ""
Write-Host "完成（GitHub / GitLab 共用脚本；Sonar 徽章 URL 不含应用版本，无需改）。" -ForegroundColor Cyan
Write-Host "建议："
Write-Host "  git diff"
Write-Host "  .\packaging\package.ps1 -SkipSigning"
Write-Host "  # GitHub 签名发版:  git tag v$NewVersion && git push <github-remote> v$NewVersion"
Write-Host "  # GitLab 内网:     提交后按私服流水线 / 本地打包即可"
Write-Host ""
