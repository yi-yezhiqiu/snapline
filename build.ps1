# ============================================================
#  晾晒工具 - 构建脚本
#  使用 Windows 自带的 .NET Framework 编译器，零外部依赖
#  用法:  powershell -ExecutionPolicy Bypass -File build.ps1
# ============================================================

$ErrorActionPreference = 'Stop'

$Root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir  = Join-Path $Root 'src'
$DistDir = Join-Path $Root 'dist'
$OutExe  = Join-Path $DistDir '晾晒工具.exe'

$Fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$Wpf = Join-Path $Fw 'WPF'
$Csc = Join-Path $Fw 'csc.exe'

if (-not (Test-Path $Csc)) {
    throw "找不到系统编译器: $Csc"
}

$refs = @(
    (Join-Path $Fw  'System.dll'),
    (Join-Path $Fw  'System.Core.dll'),
    (Join-Path $Fw  'System.Drawing.dll'),
    (Join-Path $Fw  'System.Windows.Forms.dll'),
    (Join-Path $Fw  'System.Xaml.dll'),
    (Join-Path $Wpf 'WindowsBase.dll'),
    (Join-Path $Wpf 'PresentationCore.dll'),
    (Join-Path $Wpf 'PresentationFramework.dll')
)

foreach ($r in $refs) {
    if (-not (Test-Path $r)) { throw "缺少程序集: $r" }
}

$sources = Get-ChildItem -Path $SrcDir -Filter '*.cs' -Recurse |
           Sort-Object FullName |
           Select-Object -ExpandProperty FullName

if ($sources.Count -eq 0) { throw "在 $SrcDir 下没有找到任何 .cs 源文件" }

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
if (Test-Path $OutExe) { Remove-Item $OutExe -Force }

$refArgs = $refs | ForEach-Object { "/r:$_" }

Write-Host "编译 $($sources.Count) 个源文件 ..." -ForegroundColor Cyan

$compileOutput = & $Csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 `
    "/out:$OutExe" `
    $refArgs $sources 2>&1

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OutExe)) {
    Write-Host "--- 编译失败 ---" -ForegroundColor Red
    $compileOutput | ForEach-Object { Write-Host $_ }
    exit 1
}

if ($compileOutput) {
    Write-Host "--- 编译警告 ---" -ForegroundColor Yellow
    $compileOutput | ForEach-Object { Write-Host $_ }
}

$size = (Get-Item $OutExe).Length
Write-Host ("编译成功 -> {0}  ({1:N0} 字节 / {2:N1} KB)" -f $OutExe, $size, ($size / 1KB)) -ForegroundColor Green

