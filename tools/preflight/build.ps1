<#
  构建预检工具（Preflight.dll）—— 不启动游戏，验证 MoreCharacterLines 能不能正常工作。
  它加载真实的 sts2.dll + 已安装的 mod DLL，跑 84 项断言（补丁挂点、反射目标、抽取逻辑、
  ping 的确定性/分档/通用池规则、JSON 解析……）。

  用法：
      powershell -ExecutionPolicy Bypass -File tools\preflight\build.ps1
      powershell -ExecutionPolicy Bypass -File tools\preflight\build.ps1 -GameDir "X:\...\Slay the Spire 2"

  跑（构建完按提示照抄即可）：
      cd tools\preflight\bin
      dotnet Preflight.dll "<游戏>\data_sts2_windows_x86_64" "<游戏>\mods\MoreCharacterLines\MoreCharacterLines.dll"

  为什么不用 dotnet build：本机通常只有 .NET 8 SDK + .NET 9 运行时，
  所以和 mod 一样用 SDK 自带的 Roslyn(csc) 编译，引用游戏目录里的 .NET 9 程序集。
#>
param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent (Split-Path -Parent $here)      # tools\preflight -> 仓库根
$bin  = Join-Path $here "bin"
$src  = Join-Path $here "Preflight.cs"

Write-Host ""
Write-Host "==> 定位游戏目录" -ForegroundColor Cyan
function Find-GameDir {
    $candidates = @()
    $marker = Join-Path $repo "game_dir.txt"
    if (Test-Path $marker) { $candidates += (Get-Content $marker -Raw).Trim() }
    $candidates += @(
        "E:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        "D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        "D:\Steam\steamapps\common\Slay the Spire 2",
        "D:\SteamLibrary\steamapps\common\Slay the Spire 2",
        "E:\Steam\steamapps\common\Slay the Spire 2",
        "E:\SteamLibrary\steamapps\common\Slay the Spire 2"
    )
    foreach ($c in $candidates) {
        if (-not $c) { continue }
        if (Test-Path (Join-Path $c "data_sts2_windows_x86_64\sts2.dll")) { return $c }
    }
    return ""
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir) {
    throw "没找到游戏目录。请用 -GameDir 指定，或在仓库根建一个 game_dir.txt 写上游戏根目录路径。"
}
$sts2Dir = Join-Path $GameDir "data_sts2_windows_x86_64"
if (-not (Test-Path (Join-Path $sts2Dir "sts2.dll"))) {
    throw "游戏目录里找不到 data_sts2_windows_x86_64\sts2.dll：$GameDir"
}
Write-Host "游戏目录：$GameDir"

Write-Host ""
Write-Host "==> 准备 bin" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# 预检运行时要 0Harmony.dll（游戏目录里一定有），拷到 bin 才不会 FileNotFound
Copy-Item (Join-Path $sts2Dir "0Harmony.dll") $bin -Force

# 没有 runtimeconfig 就写一份（刚 clone 下来也能直接跑；写成不带 BOM 的 UTF-8）
$runtimeConfig = Join-Path $bin "Preflight.runtimeconfig.json"
if (-not (Test-Path $runtimeConfig)) {
    $json = '{ "runtimeOptions": { "tfm": "net9.0", "rollForward": "latestMinor", "framework": { "name": "Microsoft.NETCore.App", "version": "9.0.0" } } }'
    [System.IO.File]::WriteAllText($runtimeConfig, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "已生成 $runtimeConfig"
}

Write-Host ""
Write-Host "==> 编译 $([System.IO.Path]::GetFileName($src))" -ForegroundColor Cyan
function Test-ManagedAssembly([string]$path) {
    # 游戏目录里混着 coreclr.dll / steam_api64.dll 等原生 DLL，它们不能当 -r: 引用（csc 会报 CS0009）
    try {
        [void][System.Reflection.AssemblyName]::GetAssemblyName($path)
        return $true
    } catch {
        return $false
    }
}

function Get-NewestSdkCsc {
    $pattern = Join-Path ${env:ProgramFiles} "dotnet\sdk\*\Roslyn\bincore\csc.dll"
    $items = @(Get-ChildItem $pattern -ErrorAction SilentlyContinue)
    if ($items.Count -eq 0) { return $null }
    return ($items |
        Sort-Object { try { [version](($_.FullName -split '\\sdk\\')[1].Split('\')[0]) } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1).FullName
}

$csc = Get-NewestSdkCsc
if (-not $csc) {
    throw "找不到 csc.dll，请安装 .NET SDK（https://dotnet.microsoft.com/download）后重试"
}

$refs = @()
foreach ($file in Get-ChildItem (Join-Path $sts2Dir "*.dll")) {
    if (Test-ManagedAssembly $file.FullName) { $refs += "-r:" + $file.FullName }
}
$refs += "-r:" + (Join-Path $bin "0Harmony.dll")
Write-Host "引用托管程序集 $($refs.Count) 个"

$out = Join-Path $bin "Preflight.dll"
$cscArgs = @(
    $csc, "-nologo", "-noconfig", "-nostdlib+", "-target:exe",
    "-langversion:12", "-nullable:enable", "-optimize+", "-deterministic", "-codepage:65001",
    "-out:$out"
) + $refs + @($src)

& dotnet @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc 编译失败（退出码 $LASTEXITCODE）" }

Write-Host ""
Write-Host "构建完成：$out" -ForegroundColor Green
Write-Host ""
Write-Host "跑预检（把 <游戏> 换成你的游戏根目录）：" -ForegroundColor Green
Write-Host "  cd `"$bin`""
Write-Host "  dotnet Preflight.dll `"$sts2Dir`" `"$GameDir\mods\MoreCharacterLines\MoreCharacterLines.dll`""
