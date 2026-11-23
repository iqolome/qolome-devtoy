# Qolome Devtoy 本地 SonarQube 扫描
# 依赖环境变量（勿写入仓库）:
#   SONAR_HOST_URL  SONAR_TOKEN
# 可选:
#   SONAR_SCANNER_HOME  — 无 dotnet-sonarscanner 时回退到 sonar-scanner.bat
#
# 用法:
#   .\scripts\sonar-scan.ps1
#   .\scripts\sonar-scan.ps1 -Configuration Release
#   .\scripts\sonar-scan.ps1 -ScannerCli   # 强制使用 sonar-scanner CLI（不跑 MSBuild 分析）

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [ValidateSet("x86", "x64", "ARM64")]
    [string] $Platform = "x64",

    [switch] $ScannerCli,

    [switch] $SkipInstallTool
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

function Require-Env([string] $Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "环境变量 $Name 未设置。请配置后重试（勿把 token 写入仓库）。"
    }
    return $value
}

function Get-SonarProperty {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Key
    )
    if (-not (Test-Path $Path)) {
        throw "缺少 Sonar 配置: $Path"
    }
    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#") -or $trimmed -eq "") { continue }
        if ($trimmed -match ("^" + [regex]::Escape($Key) + "\s*=\s*(.+)$")) {
            return $Matches[1].Trim()
        }
    }
    throw "sonar-project.properties 中未找到 $Key"
}

$hostUrl = Require-Env "SONAR_HOST_URL"
$token = Require-Env "SONAR_TOKEN"

$propsPath = Join-Path $RepoRoot "sonar-project.properties"
$projectKey = Get-SonarProperty -Path $propsPath -Key "sonar.projectKey"
$projectName = Get-SonarProperty -Path $propsPath -Key "sonar.projectName"
$projectVersion = Get-SonarProperty -Path $propsPath -Key "sonar.projectVersion"

# .slnx 无 Debug|x64 等解决方案配置；与日常开发一致，直接编 App（会连带 Core）
$appProject = Join-Path $RepoRoot "src\Qolome.Devtoy.App\Qolome.Devtoy.App.csproj"
if (-not (Test-Path $appProject)) {
    throw "未找到工程: $appProject"
}

function Test-DotnetSonarScanner {
    $null -ne (Get-Command "dotnet-sonarscanner" -ErrorAction SilentlyContinue)
}

function Ensure-DotnetSonarScanner {
    if (Test-DotnetSonarScanner) { return $true }
    if ($SkipInstallTool) { return $false }
    Write-Host "==> 安装全局工具 dotnet-sonarscanner" -ForegroundColor Cyan
    dotnet tool install --global dotnet-sonarscanner
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "dotnet-sonarscanner 安装失败"
        return $false
    }
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
        [Environment]::GetEnvironmentVariable("Path", "User")
    return (Test-DotnetSonarScanner)
}

function Invoke-ScannerCli {
    $scannerHome = [Environment]::GetEnvironmentVariable("SONAR_SCANNER_HOME")
    if ([string]::IsNullOrWhiteSpace($scannerHome)) {
        throw "SONAR_SCANNER_HOME 未设置，无法使用 sonar-scanner CLI。"
    }
    $bat = Join-Path $scannerHome "bin\sonar-scanner.bat"
    if (-not (Test-Path $bat)) {
        throw "未找到 sonar-scanner: $bat"
    }
    Write-Host "==> sonar-scanner CLI: $projectKey ($projectVersion)" -ForegroundColor Cyan
    & $bat `
        "-Dsonar.host.url=$hostUrl" `
        "-Dsonar.token=$token"
    if ($LASTEXITCODE -ne 0) {
        throw "sonar-scanner 失败，退出码 $LASTEXITCODE"
    }
}

function Invoke-DotnetSonarScanner {
    Write-Host "==> SonarScanner for .NET begin: $projectKey ($projectVersion)" -ForegroundColor Cyan
    dotnet sonarscanner begin `
        /k:"$projectKey" `
        /n:"$projectName" `
        /v:"$projectVersion" `
        /d:sonar.host.url="$hostUrl" `
        /d:sonar.token="$token" `
        /d:sonar.qualitygate.wait=true `
        /d:sonar.exclusions="**/bin/**,**/obj/**,**/dist/**,**/*.g.cs,**/*.g.i.cs,**/Generated Files/**"

    if ($LASTEXITCODE -ne 0) {
        throw "sonarscanner begin 失败，退出码 $LASTEXITCODE"
    }

    $buildOk = $false
    try {
        Write-Host "==> dotnet build App ($Configuration|$Platform)" -ForegroundColor Cyan
        dotnet build $appProject -c $Configuration -p:Platform=$Platform -r "win-$Platform" --no-incremental
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build 失败，退出码 $LASTEXITCODE"
        }
        $buildOk = $true
    }
    finally {
        Write-Host "==> SonarScanner for .NET end" -ForegroundColor Cyan
        dotnet sonarscanner end /d:sonar.token="$token"
        if ($LASTEXITCODE -ne 0 -and $buildOk) {
            throw "sonarscanner end 失败，退出码 $LASTEXITCODE"
        }
    }
}

$useCli = [bool]$ScannerCli
if (-not $useCli) {
    if (-not (Ensure-DotnetSonarScanner)) {
        Write-Warning "未可用的 dotnet-sonarscanner，回退到 sonar-scanner CLI（C# 规则分析可能不完整）"
        $useCli = $true
    }
}

if ($useCli) {
    Invoke-ScannerCli
}
else {
    Invoke-DotnetSonarScanner
}

Write-Host "==> Sonar 扫描完成: $projectKey" -ForegroundColor Green
