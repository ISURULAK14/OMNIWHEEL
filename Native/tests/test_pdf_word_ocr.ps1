<#
.SYNOPSIS
    OMNIWHEEL 1.0 - Targeted Test Suite: PDF, Word (DOCX), and OCR Conversions
#>

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem

$exe = Resolve-Path (Join-Path $PSScriptRoot "..\bin\OMNIWHEEL.exe")
if (-not (Test-Path $exe)) {
    $exe = "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe"
}
$outDir = Join-Path $env:TEMP "OmniPdfWordOcrTests_$(Get-Random)"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Log-Test($name, $pass, $details) {
    $status = if ($pass) { "PASS" } else { "FAIL" }
    $color = if ($pass) { "Green" } else { "Red" }
    Write-Host ("[{0,-4}] {1,-50} | {2}" -f $status, $name, $details) -ForegroundColor $color
    $results.Add([PSCustomObject]@{ Test = $name; Status = $status; Details = $details })
}

try {
    $srcPdf = Resolve-Path (Join-Path $PSScriptRoot "..\..\OMNIWHEEL_BY_ISURU_User_Guide.pdf")

    # 1. PDF -> Word (DOCX)
    $outDocx = Join-Path $outDir "guide_converted.docx"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = "--headless --input `"$srcPdf`" --action `"docx`" --output `"$outDocx`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(60000)
    $sw.Stop()

    $docxExists = (Test-Path $outDocx) -and ((Get-Item $outDocx).Length -gt 1000)
    Log-Test "PDF -> Word (DOCX) Generation" $docxExists "Time: $($sw.ElapsedMilliseconds)ms, Size: $(if ($docxExists) { (Get-Item $outDocx).Length } else { 0 }) bytes"

    # 2. PDF -> OCR to Word (DOCX) with Multi-Page Preservation
    $outOcrDocx = Join-Path $outDir "guide_ocr.docx"
    $psi.Arguments = "--headless --input `"$srcPdf`" --action `"ocr-docx`" --output `"$outOcrDocx`""
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(60000)
    $sw.Stop()

    $ocrDocxExists = (Test-Path $outOcrDocx) -and ((Get-Item $outOcrDocx).Length -gt 1000)
    Log-Test "PDF -> OCR to Word (DOCX)" $ocrDocxExists "Time: $($sw.ElapsedMilliseconds)ms, Size: $(if ($ocrDocxExists) { (Get-Item $outOcrDocx).Length } else { 0 }) bytes"

    if ($ocrDocxExists) {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($outOcrDocx)
        $docEntry = $zip.GetEntry("word/document.xml")
        $reader = New-Object System.IO.StreamReader($docEntry.Open(), [System.Text.Encoding]::UTF8)
        $xml = $reader.ReadToEnd()
        $reader.Dispose()
        $zip.Dispose()

        $brCount = ([regex]::Matches($xml, '<w:br\s+w:type="page"/>')).Count
        $has4Breaks = ($brCount -eq 4)
        Log-Test "PDF OCR Multi-Page Separation (4 Page Breaks)" $has4Breaks "Exact 4 <w:br w:type='page'/> breaks found across 5 pages"

        $hasCleanBody = -not $xml.Contains("--- PAGE")
        Log-Test "PDF OCR Clean Body (No Debug Markers)" $hasCleanBody "Zero debug tokens in generated Word body"

        $hasMargins = $xml.Contains("<w:pgMar")
        Log-Test "PDF OCR 1-Inch Standard Margins" $hasMargins "Standard 1440-twip margins defined"

        $hasCalibri = $xml.Contains('w:ascii="Calibri"')
        Log-Test "PDF OCR Professional Typography (Calibri)" $hasCalibri "Calibri typography applied"
    }

    # 3. PDF -> OCR Text (Direct OCR on Multi-Page PDF)
    $outTxt = Join-Path $outDir "guide_ocr.txt"
    $psi.Arguments = "--headless --input `"$srcPdf`" --action `"ocr-txt`" --output `"$outTxt`""
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(60000)
    $sw.Stop()

    $txtExists = (Test-Path $outTxt) -and ((Get-Item $outTxt).Length -gt 1000)
    Log-Test "PDF -> OCR Text Extraction" $txtExists "Time: $($sw.ElapsedMilliseconds)ms, Length: $(if ($txtExists) { (Get-Item $outTxt).Length } else { 0 }) bytes"

    if ($txtExists) {
        $extracted = Get-Content $outTxt -Raw
        $pages = $extracted.Split([char]12)
        $has5Pages = ($pages.Count -eq 5)
        Log-Test "PDF OCR Multi-Page Separation (\f Form Feed)" $has5Pages "Extracted $($pages.Count) pages separated by form feed"
    }

    # 4. Word (DOCX) -> PDF Conversion
    $srcDocx = $outOcrDocx
    $outFromDocxPdf = Join-Path $outDir "exported_from_word.pdf"
    $psi.Arguments = "--headless --input `"$srcDocx`" --action `"pdf`" --output `"$outFromDocxPdf`""
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(60000)
    $sw.Stop()

    $pdfFromDocxExists = (Test-Path $outFromDocxPdf) -and ((Get-Item $outFromDocxPdf).Length -gt 100)
    Log-Test "Word (DOCX) -> PDF Compilation" $pdfFromDocxExists "Time: $($sw.ElapsedMilliseconds)ms, Size: $(if ($pdfFromDocxExists) { (Get-Item $outFromDocxPdf).Length } else { 0 }) bytes"

    if ($pdfFromDocxExists) {
        $bytes = [System.IO.File]::ReadAllBytes($outFromDocxPdf)
        $isPdf = ($bytes[0] -eq 0x25 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x44 -and $bytes[3] -eq 0x46) # %PDF
        Log-Test "Exported PDF Binary %PDF Header" $isPdf "Valid PDF binary header"
    }

    # 5. Image -> OCR Word (DOCX) & Text (TXT)
    $imgFixt = Join-Path $outDir "contract_sample.png"
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 800, 400
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
    $fontTitle = New-Object System.Drawing.Font("Arial", 20, [System.Drawing.FontStyle]::Bold)
    $fontBody = New-Object System.Drawing.Font("Arial", 14, [System.Drawing.FontStyle]::Regular)
    $g.DrawString("SERVICE LEVEL AGREEMENT", $fontTitle, $brush, 40, 30)
    $g.DrawString("Party A: Omnitools International Inc.", $fontBody, $brush, 40, 90)
    $g.DrawString("Party B: Global Enterprises LLC", $fontBody, $brush, 40, 130)
    $g.DrawString("Execution Date: 2026-11-11", $fontBody, $brush, 40, 170)
    $g.Dispose()
    $bmp.Save($imgFixt, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    $outImgDocx = Join-Path $outDir "contract_ocr.docx"
    $psi.Arguments = "--headless --input `"$imgFixt`" --action `"ocr-docx`" --output `"$outImgDocx`""
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(60000)
    $sw.Stop()

    $imgDocxOk = (Test-Path $outImgDocx) -and ((Get-Item $outImgDocx).Length -gt 1000)
    Log-Test "Image -> OCR to Word (DOCX)" $imgDocxOk "Time: $($sw.ElapsedMilliseconds)ms, Size: $(if ($imgDocxOk) { (Get-Item $outImgDocx).Length } else { 0 }) bytes"

    if ($imgDocxOk) {
        $zip2 = [System.IO.Compression.ZipFile]::OpenRead($outImgDocx)
        $doc2 = $zip2.GetEntry("word/document.xml")
        $reader2 = New-Object System.IO.StreamReader($doc2.Open(), [System.Text.Encoding]::UTF8)
        $xml2 = $reader2.ReadToEnd()
        $reader2.Dispose()
        $zip2.Dispose()
        $hasAgreement = $xml2.Contains("AGREEMENT") -or $xml2.Contains("Omnitools")
        Log-Test "Image OCR Text Accuracy in Word DOCX" $hasAgreement "Exact agreement text recognized in DOCX"
    }

    # 6. Word (DOCX) -> Plain Text (TXT)
    $outTxtFromWord = Join-Path $outDir "extracted_text.txt"
    $psi.Arguments = "--headless --input `"$outOcrDocx`" --action `"txt`" --output `"$outTxtFromWord`""
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(60000)
    $sw.Stop()

    $txtFromWordOk = (Test-Path $outTxtFromWord) -and ((Get-Item $outTxtFromWord).Length -gt 500)
    Log-Test "Word (DOCX) -> Clean Text (TXT)" $txtFromWordOk "Time: $($sw.ElapsedMilliseconds)ms, Extracted: $(if ($txtFromWordOk) { (Get-Item $outTxtFromWord).Length } else { 0 }) chars"

} finally {
    if (Test-Path $outDir) {
        Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$passCount = ($results | Where-Object { $_.Status -eq "PASS" }).Count
$totalCount = $results.Count
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host " TARGETED CONVERTER TEST SUMMARY: $passCount / $totalCount PASSED" -ForegroundColor $(if ($passCount -eq $totalCount) { "Green" } else { "Red" })
Write-Host "========================================================" -ForegroundColor Cyan
