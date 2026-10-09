# ============================================================
#  Snapline 发布脚本
#
#  用法（在你自己电脑上，普通 PowerShell 即可，不需要管理员）：
#      powershell -ExecutionPolicy Bypass -File PUBLISH.ps1
#
#  它会问你要一个 GitHub token（输入时不回显），然后自动：
#    1. 推送 v1.0.0 标签
#    2. 创建 Release（用 RELEASE_NOTES.md 作为说明）
#    3. 上传 晾晒工具.exe 作为附件
#
#  也可以直接传参（适合脚本化调用；token 会进命令历史，不推荐）：
#      powershell -ExecutionPolicy Bypass -File PUBLISH.ps1 -Token ghp_xxx
#
#  token 需要 "repo" 权限（classic token 勾 repo 即可）。
#  token 只在内存里用一次，不写入任何文件。
#  用完请去 GitHub 把它删掉：Settings → Developer settings → Personal access tokens
# ============================================================

param(
    [string]$Token
)

$ErrorActionPreference = 'Stop'

$Repo      = 'yi-yezhiqiu/snapline'
$Tag       = 'v1.0.0'
$ReleaseNm = 'Snapline v1.0.0 · 晾晒工具'
$Asset     = Join-Path $PSScriptRoot 'dist\晾晒工具.exe'
$Notes     = Join-Path $PSScriptRoot 'RELEASE_NOTES.md'

Write-Host ""
Write-Host "=== Snapline 发布 ===" -ForegroundColor Cyan
Write-Host "仓库: $Repo"
Write-Host "标签: $Tag"
Write-Host ""

# ---------- 检查文件 ----------
if (-not (Test-Path $Asset)) { throw "找不到 $Asset，请先运行 build.ps1" }
$exeInfo = Get-Item $Asset
Write-Host ("附件: {0}  ({1:N0} 字节)" -f $exeInfo.Name, $exeInfo.Length) -ForegroundColor Green
if (-not (Test-Path $Notes)) { throw "找不到 $Notes" }

# ---------- 取 token ----------
if ([string]::IsNullOrWhiteSpace($Token)) {
    $sec = Read-Host "粘贴 GitHub token（输入时不会回显）" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try {
        $Token = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}
if ([string]::IsNullOrWhiteSpace($Token)) { throw "没有输入 token" }

$Headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'snapline-publish-script'
}

# TLS：有些网络环境下需要显式指定
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---------- 1. 推标签 ----------
Write-Host ""
Write-Host "[1/3] 推送标签 $Tag ..." -ForegroundColor Yellow
Push-Location $PSScriptRoot
try {
    git push origin $Tag 2>&1 | ForEach-Object { Write-Host "      $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "      标签推送失败（可能已存在），继续尝试建 Release" -ForegroundColor DarkYellow
    }
} finally { Pop-Location }

# ---------- 2. 建 Release ----------
Write-Host ""
Write-Host "[2/3] 创建 Release ..." -ForegroundColor Yellow
$body = Get-Content $Notes -Raw -Encoding UTF8
$payload = @{
    tag_name         = $Tag
    name             = $ReleaseNm
    body             = $body
    draft            = $false
    prerelease       = $false
} | ConvertTo-Json -Depth 5 -Compress

try {
    $release = Invoke-RestMethod -Method Post `
        -Uri "https://api.github.com/repos/$Repo/releases" `
        -Headers $Headers -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($payload))
    Write-Host "      已创建: $($release.html_url)" -ForegroundColor Green
}
catch {
    $msg = $_.Exception.Message
    if ($msg -match 'already_exists') {
        Write-Host "      Release 已存在，改为取回它" -ForegroundColor DarkYellow
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $Headers
    } else {
        throw
    }
}

# ---------- 3. 上传附件 ----------
Write-Host ""
Write-Host "[3/3] 上传附件 ..." -ForegroundColor Yellow
$uploadUrl = ($release.upload_url -replace '\{.*\}', '')
$name = [Uri]::EscapeDataString($exeInfo.Name)
$url = "$uploadUrl?name=$name"

try {
    $asset = Invoke-RestMethod -Method Post -Uri $url `
        -Headers $Headers -ContentType 'application/octet-stream' `
        -InFile $Asset
    Write-Host ("      已上传: {0}  ({1:N0} 字节)" -f $asset.name, $asset.size) -ForegroundColor Green
    Write-Host "      下载地址: $($asset.browser_download_url)" -ForegroundColor Green
}
catch {
    Write-Host "      上传失败: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "      Release 已建好，可以手动把 exe 拖到网页上" -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host "Release 页面: $($release.html_url)"
Write-Host ""
Write-Host "别忘了去删掉刚才那个 token：" -ForegroundColor Magenta
Write-Host "  https://github.com/settings/tokens"
Write-Host ""

# ---------- 清掉内存里的 token ----------
$Token = $null
$Headers = $null
[GC]::Collect()


