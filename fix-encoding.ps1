# ============================================================================
#  fix-encoding.ps1
#
#  Windows PowerShell 5.1 reads .ps1 files WITHOUT a BOM as ANSI/GBK,
#  which corrupts non-ASCII text and causes weird syntax errors
#  ("Unexpected token ...") in build.ps1.
#
#  This script makes sure every *.ps1 next to it is saved as
#  UTF-8 **with BOM**. Files that are not valid UTF-8 are left untouched.
#
#  Usage:
#      powershell -ExecutionPolicy Bypass -File fix-encoding.ps1
#
#  (Keep this file ASCII-only so it always parses, whatever the encoding.)
# ============================================================================

$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)

Get-ChildItem -Path $dir -Filter *.ps1 -File | ForEach-Object {
    $file = $_
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($hasBom) {
        Write-Host "[ok]    $($file.Name)  (already UTF-8 with BOM)"
        return
    }

    try {
        $text = $utf8Strict.GetString($bytes)
    } catch {
        Write-Host "[skip]  $($file.Name)  (not valid UTF-8, left untouched)" -ForegroundColor Yellow
        return
    }

    [System.IO.File]::WriteAllText($file.FullName, $text, $utf8Bom)
    Write-Host "[fixed] $($file.Name)  -> UTF-8 with BOM" -ForegroundColor Green
}
