<#
  MoreCharacterLines 构建脚本（Windows）
  用法：
      powershell -ExecutionPolicy Bypass -File build.ps1
      powershell -ExecutionPolicy Bypass -File build.ps1 -GameDir "X:\...\Slay the Spire 2"
      powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir "D:\somewhere"     # 只编译，不装进游戏目录

  步骤：
    1) 编译 MoreCharacterLines.dll
       （优先 dotnet build；如果机器上只有 .NET 8 SDK，则用 SDK 自带的 Roslyn csc + 游戏目录里的 .NET 9 运行时程序集直接编译）
    2) 把 assets/ 和台词 JSON 打包成 MoreCharacterLines.pck
    3) 把 MoreCharacterLines.dll / .pck / <id>.json / lines.json 复制到 游戏目录\mods\MoreCharacterLines\
#>
param(
    [string]$GameDir = "",
    [string]$OutDir = "",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$modId = "MoreCharacterLines"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step([string]$text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }

# ── 1. 找游戏目录 ─────────────────────────────────────────────────────────────
function Find-GameDir {
    $candidates = @()
    $marker = Join-Path $projectDir "game_dir.txt"
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

Write-Step "定位游戏目录"
if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir) {
    throw "没找到游戏目录。请用 -GameDir 指定，或在本目录下建一个 game_dir.txt 写上游戏根目录路径。"
}
$sts2Dir = Join-Path $GameDir "data_sts2_windows_x86_64"
if (-not (Test-Path (Join-Path $sts2Dir "sts2.dll"))) {
    throw "游戏目录里找不到 data_sts2_windows_x86_64\sts2.dll：$GameDir"
}
Write-Host "游戏目录：$GameDir"

# ── 1.5 校验台词文件（少逗号/少引号这类手滑会在这里被拦下来，而不是静默退回旧文本）──
Write-Step "校验台词文件"
$linesJson = Join-Path $projectDir "assets\$modId\lines.json"
$checkScript = Join-Path $projectDir "check_lines.py"
if (-not (Test-Path $checkScript)) { throw "缺少 check_lines.py" }
& python $checkScript $linesJson
if ($LASTEXITCODE -ne 0) {
    throw "lines.json 语法有误（见上面的行号），已中止构建。"
}

# ── 2. 编译 DLL ──────────────────────────────────────────────────────────────
Write-Step "编译 $modId.dll"

function Test-ManagedAssembly([string]$path) {
    # 游戏目录里混着 coreclr.dll / steam_api64.dll 等原生 DLL，它们不能作为 -r: 引用（csc 会报 CS0009）。
    # 用元数据读取判断是否为托管程序集。
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
    return ($items | Sort-Object { [int]($_.FullName -split '\\sdk\\')[1].Split('\')[0].Split('.')[0] } -Descending | Select-Object -First 1).FullName
}

function Test-DotnetSdk9 {
    try {
        $sdks = & dotnet --list-sdks 2>$null
    } catch { return $false }
    foreach ($line in @($sdks)) {
        if ($line -match '^\s*(\d+)\.') { if ([int]$Matches[1] -ge 9) { return $true } }
    }
    return $false
}

if (Test-DotnetSdk9) {
    Write-Host "使用 dotnet build（已检测到 .NET 9+ SDK）"
    & dotnet build (Join-Path $projectDir "$modId.csproj") -c Release -p:Sts2Dir="$sts2Dir" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败（退出码 $LASTEXITCODE）" }
    $dllPath = Join-Path $projectDir "bin\Release\net9.0\$modId.dll"
} else {
    Write-Host "未检测到 .NET 9+ SDK，改用 Roslyn(csc) + 游戏自带 .NET 9 运行时程序集编译"
    $csc = Get-NewestSdkCsc
    if (-not $csc) { throw "找不到 csc.dll，请安装 .NET 9 SDK（https://dotnet.microsoft.com/download/dotnet/9.0）后重试" }

    $cscOutDir = Join-Path $projectDir "bin\csc"
    New-Item -ItemType Directory -Force -Path $cscOutDir | Out-Null
    $dllPath = Join-Path $cscOutDir "$modId.dll"

    $refs = @()
    foreach ($file in Get-ChildItem (Join-Path $sts2Dir "*.dll")) {
        if (Test-ManagedAssembly $file.FullName) { $refs += "-r:" + $file.FullName }
    }
    Write-Host "引用托管程序集 $($refs.Count) 个"
    $sources = @(Get-ChildItem -Path $projectDir -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
        ForEach-Object { $_.FullName })
    if ($sources.Count -eq 0) { throw "$projectDir 下没找到 .cs 源文件" }

    $cscArgs = @(
        $csc, "-nologo", "-noconfig", "-nostdlib+", "-target:library",
        "-langversion:12", "-nullable:enable", "-optimize+", "-deterministic",
        "-codepage:65001",
        "-out:$dllPath"
    ) + $refs + $sources

    & dotnet @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "csc 编译失败（退出码 $LASTEXITCODE）" }
}
Write-Host "DLL：$dllPath"

# ── 3. 打包 PCK ──────────────────────────────────────────────────────────────
Write-Step "打包 PCK"

$packScript = Join-Path $projectDir "pack_godot_pck.py"
if (-not (Test-Path $packScript)) { throw "缺少 pack_godot_pck.py" }

$workDir = Join-Path $projectDir "bin\_pck_src"
if (Test-Path $workDir) { Remove-Item $workDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
Copy-Item (Join-Path $projectDir "assets\*") $workDir -Recurse -Force

$pckPath = Join-Path $projectDir "bin\$modId.pck"
& python $packScript $workDir -o $pckPath --engine-version 4.5.1 --pack-version 3
if ($LASTEXITCODE -ne 0) { throw "PCK 打包失败（退出码 $LASTEXITCODE）" }

# ── 4. 安装到 游戏目录\mods\<ModId>\ ────────────────────────────────────────
Write-Step "安装"

if (-not $OutDir) {
    if ($SkipInstall) {
        $OutDir = Join-Path $projectDir "dist\$modId"
    } else {
        $OutDir = Join-Path $GameDir "mods\$modId"
    }
}
$installOk = $true

# 组装要放进 mod 文件夹的东西：dll + pck + 清单 + 可直接编辑的台词文件 + 说明
function Install-Files([string]$dest) {
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dllPath (Join-Path $dest "$modId.dll") -Force
    Copy-Item $pckPath (Join-Path $dest "$modId.pck") -Force
    # 游戏按 <id>.json 找清单（注意：不是 mod_manifest.json）
    Copy-Item (Join-Path $projectDir "mod_manifest.json") (Join-Path $dest "$modId.json") -Force

    # lines.json：随包附带的“可就地修改”台词文件。
    # 已存在就不覆盖（避免吞掉别人改好的台词）；若老文件没有 UTF-8 BOM 则补一个，
    # 这样记事本 / PowerShell 这类 Windows 工具打开中文不会乱码。
    $linesDest = Join-Path $dest "lines.json"
    if (-not (Test-Path $linesDest)) {
        $text = [System.IO.File]::ReadAllText((Join-Path $projectDir "assets\$modId\lines.json"), (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::WriteAllText($linesDest, $text, (New-Object System.Text.UTF8Encoding($true)))
        Write-Host "已放入可编辑台词文件：$linesDest"
    } else {
        $bytes = [System.IO.File]::ReadAllBytes($linesDest)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        if ($hasBom) {
            Write-Host "保留已有的台词文件（不覆盖）：$linesDest"
        } else {
            # 只有确认是合法 UTF-8 才动它，避免把 GBK 之类的文件改坏
            try {
                $strict = New-Object System.Text.UTF8Encoding($false, $true)
                $text = $strict.GetString($bytes)
                [System.IO.File]::WriteAllText($linesDest, $text, (New-Object System.Text.UTF8Encoding($true)))
                Write-Host "已保留原有台词内容，并补上 UTF-8 BOM：$linesDest" -ForegroundColor Yellow
            } catch {
                Write-Host "保留已有的台词文件（不是 UTF-8，未改动）：$linesDest" -ForegroundColor Yellow
            }
        }
    }

    # 说明文档也一起放进去，方便别人打开 mod 文件夹就知道怎么改
    foreach ($doc in @("README.md", "DESIGN.md", "THIRD_PARTY.md")) {
        $src = Join-Path $projectDir $doc
        if (Test-Path $src) { Copy-Item $src (Join-Path $dest $doc) -Force }
    }
}

try {
    Install-Files $OutDir
} catch {
    $installOk = $false
    $fallback = Join-Path $projectDir "dist\$modId"
    Install-Files $fallback
    Write-Host "写入游戏目录失败（$($_.Exception.Message)），已改为输出到：$fallback" -ForegroundColor Yellow
    $OutDir = $fallback
}

Write-Host ""
if ($SkipInstall) {
    Write-Host "只构建未安装，产物在：$OutDir" -ForegroundColor Green
} elseif ($installOk) {
    Write-Host "构建完成，已安装到：$OutDir" -ForegroundColor Green
    Write-Host "改台词不用重新编译：编辑 $OutDir\lines.json（或 %AppData%\SlayTheSpire2\MoreCharacterLines\lines.json），然后重进一次火堆。" -ForegroundColor Green
} else {
    Write-Host "构建完成，产物在：$OutDir" -ForegroundColor Green
    Write-Host "请把该目录整体复制到 游戏目录\mods\ 下。" -ForegroundColor Yellow
}
