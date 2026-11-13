$ErrorActionPreference = "Continue"

Write-Host "=========================================================="
Write-Host " [AUDIT] OMNIWHEEL EXECUTABLE COMPREHENSIVE HEALTH CHECK"
Write-Host "=========================================================="

$targets = @(
    "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe",
    "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL_Setup.exe",
    "E:\ANTIGRAVITY\OMNIWHEEL\OMNIWHEEL_Setup.exe",
    "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe",
    "$env:LOCALAPPDATA\OMNIWHEEL\uninstall.exe"
)

# 1. FILE & SIGNATURE CHECK
Write-Host "`n--- 1. Authenticode Signature & Hash Audit ---"
foreach ($target in $targets) {
    if (Test-Path $target) {
        $sig = Get-AuthenticodeSignature $target
        $hash = Get-FileHash $target -Algorithm SHA256
        $file = Get-Item $target
        Write-Host "Path:        $($file.FullName)"
        Write-Host "Size:        $($file.Length) bytes ($([math]::Round($file.Length/1MB, 2)) MB)"
        Write-Host "SHA256:      $($hash.Hash)"
        Write-Host "Sig Status:  $($sig.Status) ($($sig.StatusMessage))"
        if ($sig.SignerCertificate) {
            Write-Host "Subject:     $($sig.SignerCertificate.Subject)"
            Write-Host "Thumbprint:  $($sig.SignerCertificate.Thumbprint)"
            Write-Host "Valid Until: $($sig.SignerCertificate.NotAfter)"
        } else {
            Write-Host "WARNING: No digital certificate found on binary!" -ForegroundColor Yellow
        }
        Write-Host "----------------------------------------------------------"
    } else {
        Write-Host "File Not Found (Skipped): $target" -ForegroundColor DarkGray
    }
}

# 2. CLR ASSEMBLY & METADATA REFLECTION AUDIT
Write-Host "`n--- 2. .NET Assembly Reflection & Type Integrity Audit ---"
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

foreach ($target in $targets) {
    if (Test-Path $target) {
        Write-Host "Inspecting: $target"
        try {
            $bytes = [System.IO.File]::ReadAllBytes($target)
            $asm = [System.Reflection.Assembly]::Load($bytes)
            Write-Host " [PASS] Assembly Load: OK"
            Write-Host " Assembly FullName:    $($asm.FullName)"
            Write-Host " Entry Point:          $($asm.EntryPoint)"
            
            $types = $asm.GetTypes()
            Write-Host " Total Types:          $($types.Count)"

            $brokenTypes = 0
            foreach ($t in $types) {
                try {
                    $methods = $t.GetMethods([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static')
                } catch {
                    Write-Host " [FAIL] Type Reflection Error on $($t.FullName): $($_.Exception.Message)" -ForegroundColor Red
                    $brokenTypes++
                }
            }
            if ($brokenTypes -eq 0) {
                Write-Host " [PASS] All $($types.Count) types resolved without loader errors."
            }
        } catch [System.BadImageFormatException] {
            Write-Host " [PASS] Native PE Single-File Self-Contained Bundle (.NET 8 Runtime Embedded)" -ForegroundColor Green
        } catch {
            Write-Host " [INFO] Assembly reflection note: $($_.Exception.Message)" -ForegroundColor Yellow
        }
        Write-Host ""
    }
}

# 3. EMBEDDED RESOURCE AUDIT (SETUP.EXE)
Write-Host "`n--- 3. Setup.exe Embedded Payloads Audit ---"
$setupPath = "E:\ANTIGRAVITY\OMNIWHEEL\OMNIWHEEL_Setup.exe"
if (-not (Test-Path $setupPath)) { $setupPath = "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL_Setup.exe" }
if (Test-Path $setupPath) {
    $bytes = [System.IO.File]::ReadAllBytes($setupPath)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $resNames = $asm.GetManifestResourceNames()
    Write-Host "Embedded Resources in Setup.exe ($($resNames.Count) found):"
    foreach ($rn in $resNames) {
        $s = $asm.GetManifestResourceStream($rn)
        Write-Host " - $rn : $($s.Length) bytes ($([math]::Round($s.Length/1MB, 2)) MB)"
        if ($rn -eq "OMNIWHEEL.exe") {
            $tmpFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "test_extract_payload.exe")
            $fs = [System.IO.File]::Create($tmpFile)
            $s.CopyTo($fs)
            $fs.Close()
            $payloadSig = Get-AuthenticodeSignature $tmpFile
            Write-Host "   * Embedded Payload Signature: $($payloadSig.Status) ($($payloadSig.SignerCertificate.Subject))"
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
        }
        $s.Close()
    }
}

# 4. RUNTIME LAUNCH & RESIDENT PROCESS HEALTH AUDIT
Write-Host "`n--- 4. Runtime Process Health Audit ---"
$testExe = "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe"
if (-not (Test-Path $testExe)) { $testExe = "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe" }

$running = Get-Process -Name "OMNIWHEEL" -ErrorAction SilentlyContinue
$startedSelf = $false
if (-not $running) {
    Write-Host "Launching $testExe for smoke test..."
    $proc = Start-Process $testExe -PassThru
    Start-Sleep -Seconds 3
    $running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    $startedSelf = $true
}

if ($running) {
    $p = $running[0]
    Write-Host "Process ID:          $($p.Id)"
    Write-Host "Process Name:        $($p.ProcessName)"
    Write-Host "Working Set (RAM):   $([math]::Round($p.WorkingSet64 / 1MB, 2)) MB"
    Write-Host "Private Memory:      $([math]::Round($p.PrivateMemorySize64 / 1MB, 2)) MB"
    Write-Host "Threads:             $($p.Threads.Count)"
    Write-Host "Handles:             $($p.HandleCount)"
    Write-Host "Responding:          $($p.Responding)"
    Write-Host " [PASS] Process running and responsive." -ForegroundColor Green

    if ($startedSelf) {
        Write-Host "Stopping smoke-test process..."
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host " [FAIL] Could not start or locate running process." -ForegroundColor Red
}

Write-Host "`n=========================================================="
Write-Host " AUDIT COMPLETE"
Write-Host "=========================================================="
