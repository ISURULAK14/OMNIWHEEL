$certName = "CN=ISURU GUNASEKERA, O=OMNIWHEEL Tools"
$cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -match "ISURU GUNASEKERA" } | Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating new SHA256 Code Signing Certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certName -CertStoreLocation "Cert:\CurrentUser\My" -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(10)
}

Write-Host "Using Certificate: $($cert.Subject) [Thumbprint: $($cert.Thumbprint)]" -ForegroundColor Green

$PSScriptRootNative = $PSScriptRoot
$distRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cerPath = Join-Path $distRoot "OMNIWHEEL_Publisher.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Public certificate exported: $cerPath" -ForegroundColor Green

# Ensure certificate is recognized in CurrentUser TrustedPublisher store
try {
    $tpStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "CurrentUser")
    $tpStore.Open("ReadWrite")
    if (-not ($tpStore.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) {
        $tpStore.Add($cert)
    }
    $tpStore.Close()
} catch { }

# Sign binaries
$targets = @(
    (Join-Path $PSScriptRoot "bin\OMNIWHEEL.exe"),
    (Join-Path $PSScriptRoot "bin\OMNIWHEEL_Setup.exe"),
    (Join-Path $distRoot "OMNIWHEEL_Setup.exe"),
    "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe",
    "$env:LOCALAPPDATA\OMNIWHEEL\uninstall.exe"
)

foreach ($target in $targets) {
    if (Test-Path $target) {
        Write-Host "Signing $target..." -ForegroundColor Cyan
        try {
            $sig = Set-AuthenticodeSignature -FilePath $target -Certificate $cert -HashAlgorithm SHA256 -ErrorAction Stop
            Write-Host "Signed: $target (Status: $($sig.Status))" -ForegroundColor Green
        } catch {
            Write-Host "Note: Could not re-sign $target (file may be in use by background process): $_" -ForegroundColor Yellow
        }
    }
}
