# Master Test Runner for OMNIWHEEL
$ErrorActionPreference = "Stop"

Write-Host "Running Action Test Suite..." -ForegroundColor Cyan
& "$PSScriptRoot\tests\test_all_wheel_actions.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Action tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nRunning UI State & Architecture Test Suite..." -ForegroundColor Cyan
& "$PSScriptRoot\tests\test_radial_ui_states.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "UI state tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nRunning Comprehensive UI/UX Test Suite..." -ForegroundColor Cyan
& "$PSScriptRoot\tests\run_ui_ux_test.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "UI/UX tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nRunning Targeted PDF, Word & High-Res OCR Test Suite..." -ForegroundColor Cyan
& "$PSScriptRoot\tests\test_pdf_word_ocr.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "PDF/Word/OCR tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nRunning End-to-End Automated Test Suite..." -ForegroundColor Cyan
& "$PSScriptRoot\test_suite.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "End-to-end tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n>>> ALL TEST SUITES PASSED WITH EXIT CODE 0 <<<" -ForegroundColor Green
exit 0
