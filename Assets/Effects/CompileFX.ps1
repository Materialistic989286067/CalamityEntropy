# 编译 Assets/Effects 下的 HLSL (.fx) 着色器为 .fxc
#
# 本机没有 pwsh。调用方直接跑下面的命令，不要自己 Test-Path / 猜测 fxc 路径
# （$Compiler 默认值已写死；找不到时本脚本会报 [错误] 并 exit 1）。
#
# 用法（按名字重编译并覆盖，无需手动删 .fxc）：
#   powershell -ExecutionPolicy Bypass -File Assets/Effects/CompileFX.ps1 DestroyerBeam
#   powershell -ExecutionPolicy Bypass -File Assets/Effects/CompileFX.ps1 DestroyerBeam TwinsDeathRayBeam   #多个
#   powershell -ExecutionPolicy Bypass -File Assets/Effects/CompileFX.ps1 Destroyer*                        #通配
# 批量补齐（仅编译缺少 .fxc/.xnb 的，向后兼容旧行为）：
#   powershell -ExecutionPolicy Bypass -File Assets/Effects/CompileFX.ps1
# 全量强制重编译：
#   powershell -ExecutionPolicy Bypass -File Assets/Effects/CompileFX.ps1 -All
# 结尾暂停（手动双击想看结果时）：追加 -Pause；交互式终端会自动暂停
#
# 退出码：0=全部成功（含无需编译）；1=存在失败/缺失，便于 agent 判定

param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Name,
    [string]$Dir,
    [string]$Compiler = "C:\Users\Hommeng\Documents\My Games\Terraria\tModLoader\FXC\fxc.exe",
    [switch]$All,
    [switch]$Pause
)

#-File 相对路径调用时 PS5.1 可能令 $PSScriptRoot 为空，逐级回退定位脚本目录
if ([string]::IsNullOrWhiteSpace($Dir)) {
    if ($PSScriptRoot) { $Dir = $PSScriptRoot }
    elseif ($MyInvocation.MyCommand.Path) { $Dir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    else { $Dir = (Get-Location).Path }
}

#输出被重定向(agent 捕获)时判为非交互，绝不阻塞；真实终端/双击则暂停
$interactive = $false
try { $interactive = -not [System.Console]::IsOutputRedirected } catch { $interactive = $false }
$shouldPause = $Pause.IsPresent -or $interactive

function Exit-Script([int]$Code) {
    if ($shouldPause) {
        Write-Host ""
        [void](Read-Host "按回车键退出")
    }
    exit $Code
}

if (-not (Test-Path $Compiler)) {
    Write-Host "[错误] 找不到 fxc.exe: $Compiler" -ForegroundColor Red
    Exit-Script 1
}
if (-not (Test-Path $Dir)) {
    Write-Host "[错误] 找不到目录: $Dir" -ForegroundColor Red
    Exit-Script 1
}

#解析目标列表与是否强制覆盖：给了名字=强制重编译；否则全量(-All 强制)或仅补齐缺失
$targets = @()
$missing = 0
if ($Name -and $Name.Count -gt 0) {
    $force = $true
    foreach ($n in $Name) {
        $base = $n -replace '\.fx$', ''
        $matched = @(Get-ChildItem -Path $Dir -Filter "$base.fx" -File -ErrorAction SilentlyContinue)
        if ($matched.Count -gt 0) {
            $targets += $matched
        }
        else {
            Write-Host "[错误] 找不到着色器: $base.fx" -ForegroundColor Red
            $missing++
        }
    }
}
else {
    $force = $All.IsPresent
    $targets += @(Get-ChildItem -Path $Dir -Filter "*.fx" -File)
}
$targets = @($targets | Sort-Object FullName -Unique)

if ($targets.Count -eq 0) {
    if ($missing -gt 0) { Exit-Script 1 }
    Write-Host "[提示] 没有可编译的 .fx 文件: $Dir" -ForegroundColor Yellow
    Exit-Script 0
}

$compiled = 0
$skipped = 0
$failed = $missing

foreach ($fx in $targets) {
    $baseName = $fx.BaseName
    $xnbPath = Join-Path $Dir "$baseName.xnb"
    $fxcPath = Join-Path $Dir "$baseName.fxc"

    #非强制模式：已有产物则跳过(批量补齐)
    if (-not $force -and ((Test-Path $xnbPath) -or (Test-Path $fxcPath))) {
        $kind = if (Test-Path $xnbPath) { 'xnb' } else { 'fxc' }
        Write-Host "[跳过] $($fx.Name) (已有 $kind)" -ForegroundColor DarkGray
        $skipped++
        continue
    }

    #强制重编译先删旧 .fxc，避免编译失败时旧产物残留被误判成功
    if (Test-Path $fxcPath) { Remove-Item $fxcPath -Force }

    Write-Host "[编译] $($fx.Name) -> $baseName.fxc ... " -ForegroundColor Cyan -NoNewline
    $output = & $Compiler /nologo /T fx_2_0 /Fo $fxcPath $fx.FullName 2>&1
    if (($LASTEXITCODE -eq 0) -and (Test-Path $fxcPath)) {
        Write-Host "OK" -ForegroundColor Green
        $compiled++
    }
    else {
        Write-Host "失败" -ForegroundColor Red
        $output | Where-Object { $_ -match 'error|warning' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $failed++
    }
}

Write-Host ""
Write-Host "完成: 编译 $compiled, 跳过 $skipped, 失败 $failed" -ForegroundColor White
Exit-Script ([int]($failed -gt 0))
