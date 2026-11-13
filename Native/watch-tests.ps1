# LocalConvert Pro - Test Watcher
# Automatically rebuilds and runs test suites when C# or manifest files change

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = "E:\ANTIGRAVITY\Native"
$watcher.Filter = "*.*"
$watcher.IncludeSubdirectories = $false
$watcher.EnableRaisingEvents = $true

function Run-Suite {
    Write-Host "`n>>> File change detected! Rebuilding and running test suite... <<<" -ForegroundColor Yellow
    cmd.exe /c "E:\ANTIGRAVITY\Native\Build.bat"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n>>> Running Action Tests <<<" -ForegroundColor Cyan
        powershell -NoProfile -ExecutionPolicy Bypass -File "E:\ANTIGRAVITY\Native\tests\test_all_wheel_actions.ps1"
        Write-Host "`n>>> Running UI State Tests <<<" -ForegroundColor Cyan
        powershell -NoProfile -ExecutionPolicy Bypass -File "E:\ANTIGRAVITY\Native\tests\test_radial_ui_states.ps1"
    } else {
        Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    }
}

Write-Host "LocalConvert Pro Test Watcher active. Monitoring E:\ANTIGRAVITY\Native ... Press Ctrl+C to stop." -ForegroundColor Green
Run-Suite

$action = {
    $name = $Event.SourceEventArgs.Name
    $changeType = $Event.SourceEventArgs.ChangeType
    if ($name.EndsWith(".cs") -or $name.EndsWith(".manifest") -or $name.EndsWith(".bat")) {
        Run-Suite
    }
}

Register-ObjectEvent $watcher "Changed" -Action $action | Out-Null
Register-ObjectEvent $watcher "Created" -Action $action | Out-Null

try {
    while ($true) { Start-Sleep -Seconds 1 }
} finally {
    Unregister-Event -SourceIdentifier * -ErrorAction SilentlyContinue
    $watcher.Dispose()
}
