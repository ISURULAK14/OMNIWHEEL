$ErrorActionPreference = 'Continue'

$distDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$binDir = Resolve-Path (Join-Path $PSScriptRoot "..\bin")
$localAppDir = Join-Path $env:LOCALAPPDATA "OMNIWHEEL"

$targets = @(
    (Join-Path $distDir "OMNIWHEEL_Setup.exe"),
    (Join-Path $binDir "OMNIWHEEL.exe"),
    (Join-Path $localAppDir "OMNIWHEEL.exe"),
    (Join-Path $distDir "OMNIWHEEL_BY_ISURU_User_Guide.pdf")
)

Write-Host "=========================================="
Write-Host " 1. BINARY INTEGRITY & CHECKSUMS (SHA256) "
Write-Host "=========================================="
foreach ($t in $targets) {
    if (Test-Path $t) {
        $item = Get-Item $t
        $hash = (Get-FileHash $t -Algorithm SHA256).Hash
        $sizeMb = [math]::Round($item.Length / 1MB, 2)
        Write-Host ("[EXISTS] {0,-28} | {1,8} MB | SHA256: {2}" -f $item.Name, $sizeMb, $hash)
    } else {
        Write-Host ("[MISSING] " + $t) -ForegroundColor Red
    }
}

Write-Host "`n=========================================="
Write-Host " 2. CODE SIGNING & CERTIFICATE VALIDATION "
Write-Host "=========================================="
foreach ($t in ($targets | Where-Object { $_.EndsWith(".exe") })) {
    if (Test-Path $t) {
        $sig = Get-AuthenticodeSignature $t
        $name = [System.IO.Path]::GetFileName($t)
        Write-Host ("[SIGNATURE] {0,-25} | Status: {1,-12} | Subject: {2}" -f $name, $sig.Status, $sig.SignerCertificate.Subject)
    }
}

Write-Host "`n=========================================="
Write-Host " 3. VERSION & METADATA AUDIT (v1.0 STRICT)"
Write-Host "=========================================="
foreach ($t in ($targets | Where-Object { $_.EndsWith(".exe") })) {
    if (Test-Path $t) {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($t)
        $name = [System.IO.Path]::GetFileName($t)
        Write-Host ("[{0}]" -f $name)
        Write-Host ("   Product:     {0} (v{1})" -f $info.ProductName, $info.ProductVersion)
        Write-Host ("   FileVersion: {0}" -f $info.FileVersion)
        Write-Host ("   Company:     {0}" -f $info.CompanyName)
        Write-Host ("   Description: {0}" -f $info.FileDescription)
    }
}

Write-Host "`n=========================================="
Write-Host " 4. SMOKE TEST RUNTIMES (PE INTEGRITY)    "
Write-Host "=========================================="
$exePath = Join-Path $binDir "OMNIWHEEL.exe"
if (Test-Path $exePath) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($exePath)
        $isPE = ($bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
        $val = "CORRUPTED"
        if ($isPE) { $val = "VALID" }
        Write-Host ("[PE CHECK]  OMNIWHEEL.exe MZ header: {0}" -f $val)
    } catch {
        Write-Host ("[PE CHECK]  Failed: " + $_.Exception.Message)
    }
}

$setupPath = Join-Path $distDir "OMNIWHEEL_Setup.exe"
if (Test-Path $setupPath) {
    $bytes = [System.IO.File]::ReadAllBytes($setupPath)
    $isPE = ($bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
    $val = "CORRUPTED"
    if ($isPE) { $val = "VALID" }
    Write-Host ("[PE CHECK]  OMNIWHEEL_Setup.exe MZ header: {0} ({1} bytes)" -f $val, $bytes.Length)
}

Write-Host "`n=========================================="
Write-Host " 5. UNINSTALLER INTEGRITY                  "
Write-Host "=========================================="
$uninst = "C:\Users\isuru\AppData\Local\OMNIWHEEL\uninstall.exe"
if (Test-Path $uninst) {
    $uSig = Get-AuthenticodeSignature $uninst
    Write-Host ("[UNINSTALL] uninstall.exe present | Status: {0}" -f $uSig.Status)
} else {
    Write-Host "[UNINSTALL] Notice: uninstall.exe generated on setup install"
}

Write-Host "`n=== FINAL CHECKS COMPLETE ==="
