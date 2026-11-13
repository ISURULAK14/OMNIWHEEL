# OMNIWHEEL Automated End-to-End Test Suite
Add-Type -AssemblyName System.Drawing
$exePath = Join-Path $PSScriptRoot 'bin\OMNIWHEEL.exe'
if (-not (Test-Path $exePath)) { $exePath = "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe" }
if (-not (Test-Path $exePath)) {
    Write-Error "OMNIWHEEL.exe not found at $exePath"
    exit 1
}

$testDir = Join-Path $env:TEMP 'OMNIWHEEL_Suite'
if (Test-Path $testDir) { Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $testDir -Force | Out-Null

function Test-Conv($inFile, $targetFmt) {
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exePath
        $psi.Arguments = "--headless --input `"$inFile`" --action `"$targetFmt`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
        [void]$proc.WaitForExit(30000)

        $json = $null
        foreach ($line in ($stdout -split "`r?`n")) {
            $t = $line.Trim()
            if ($t.StartsWith("{") -and $t -match '"success"') {
                try { $json = $t | ConvertFrom-Json; break } catch { }
            }
        }

        $success = ($proc.ExitCode -eq 0 -and $json -ne $null -and $json.success -eq $true)
        $outPath = if ($json -and $json.outputPath) { $json.outputPath } else { $null }
        $exists = if ($outPath) { Test-Path $outPath } else { $false }
        $size = if ($exists) { (Get-Item $outPath).Length } else { 0 }
        $msg = if ($json -and $json.message) { $json.message } elseif ($stderr) { $stderr.Trim() } else { "Exit $($proc.ExitCode)" }

        [PSCustomObject]@{
            Input = [System.IO.Path]::GetFileName($inFile)
            Target = $targetFmt
            Success = $success
            FileCreated = $exists
            Size = $size
            OutputPath = $outPath
            Message = $msg
        }
    } catch {
        [PSCustomObject]@{
            Input = [System.IO.Path]::GetFileName($inFile)
            Target = $targetFmt
            Success = $false
            FileCreated = $false
            Size = 0
            OutputPath = $null
            Message = $_.Exception.Message
        }
    }
}

Write-Host "Creating distinct test fixtures in $testDir..." -ForegroundColor Cyan

# 1. Image
$bmp = New-Object System.Drawing.Bitmap(100, 100)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Blue)
$g.Dispose()
$imgFile = Join-Path $testDir 'test_image.png'
$bmp.Save($imgFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# 2. Text Documents
$txtFile = Join-Path $testDir 'test_doc.txt'
[System.IO.File]::WriteAllText($txtFile, "OMNIWHEEL Test Document`r`nThis is a sample document for conversion testing with human readable text and paragraphs.")

$mdFile = Join-Path $testDir 'test_readme.md'
[System.IO.File]::WriteAllText($mdFile, "# OMNIWHEEL Title`r`n`r`nThis is markdown paragraph text for format conversion testing.")

$htmlFile = Join-Path $testDir 'test_page.html'
[System.IO.File]::WriteAllText($htmlFile, '<html><body><h1>OMNIWHEEL Header</h1><p>Body paragraph text content for conversion.</p></body></html>')

# Bootstrap PDF and DOCX with distinct names
$pdfBoot = Test-Conv $txtFile 'pdf'
$pdfFile = $pdfBoot.OutputPath

$docxBoot = Test-Conv $txtFile 'docx'
$docxFile = $docxBoot.OutputPath

# 3. Data files
$csvFile = Join-Path $testDir 'test_table.csv'
[System.IO.File]::WriteAllText($csvFile, "col1,col2`r`nval1,val2")

$tsvFile = Join-Path $testDir 'test_table.tsv'
[System.IO.File]::WriteAllText($tsvFile, "col1`tcol2`r`nval1`tval2")

$jsonFile = Join-Path $testDir 'test_data.json'
[System.IO.File]::WriteAllText($jsonFile, '{"items":["item1","item2"]}')

$jsonlFile = Join-Path $testDir 'test_data.jsonl'
[System.IO.File]::WriteAllText($jsonlFile, '{"a":1}' + "`r`n" + '{"b":2}')

# 4. Subtitles
$srtFile = Join-Path $testDir 'test_sub.srt'
[System.IO.File]::WriteAllText($srtFile, "1`r`n00:00:01,000 --> 00:00:04,000`r`nHello subtitle")

$vttFile = Join-Path $testDir 'test_sub.vtt'
[System.IO.File]::WriteAllText($vttFile, "WEBVTT`r`n`r`n00:00:01.000 --> 00:00:04.000`r`nHello subtitle")

$tests = @()

# Images
Write-Host "Testing Image conversions..." -ForegroundColor Cyan
$tests += Test-Conv $imgFile 'jpeg'
$tests += Test-Conv $imgFile 'jpg'
$tests += Test-Conv $imgFile 'webp'
$tests += Test-Conv $imgFile 'bmp'
$tests += Test-Conv $imgFile 'ico'
$tests += Test-Conv $imgFile 'pdf'

# Documents
Write-Host "Testing Document conversions..." -ForegroundColor Cyan
if ($pdfFile -and (Test-Path $pdfFile)) {
    $tests += Test-Conv $pdfFile 'docx'
    $tests += Test-Conv $pdfFile 'doc'
    $tests += Test-Conv $pdfFile 'txt'
    $tests += Test-Conv $pdfFile 'html'
    $tests += Test-Conv $pdfFile 'md'
}

if ($docxFile -and (Test-Path $docxFile)) {
    $tests += Test-Conv $docxFile 'pdf'
    $tests += Test-Conv $docxFile 'txt'
    $tests += Test-Conv $docxFile 'html'
    $tests += Test-Conv $docxFile 'md'
    $tests += Test-Conv $docxFile 'doc'
}

$tests += Test-Conv $txtFile 'pdf'
$tests += Test-Conv $txtFile 'docx'
$tests += Test-Conv $txtFile 'doc'
$tests += Test-Conv $txtFile 'html'
$tests += Test-Conv $txtFile 'md'

$tests += Test-Conv $mdFile 'pdf'
$tests += Test-Conv $mdFile 'docx'
$tests += Test-Conv $mdFile 'doc'
$tests += Test-Conv $mdFile 'html'
$tests += Test-Conv $mdFile 'txt'

$tests += Test-Conv $htmlFile 'pdf'
$tests += Test-Conv $htmlFile 'docx'
$tests += Test-Conv $htmlFile 'doc'
$tests += Test-Conv $htmlFile 'txt'
$tests += Test-Conv $htmlFile 'md'

# Data
Write-Host "Testing Data conversions..." -ForegroundColor Cyan
$tests += Test-Conv $csvFile 'tsv'
$tests += Test-Conv $tsvFile 'csv'
$tests += Test-Conv $jsonFile 'jsonl'
$tests += Test-Conv $jsonlFile 'json'

# Subtitles
Write-Host "Testing Subtitle conversions..." -ForegroundColor Cyan
$tests += Test-Conv $srtFile 'vtt'
$tests += Test-Conv $srtFile 'txt'
$tests += Test-Conv $vttFile 'srt'

# Archives
Write-Host "Testing Archive conversions..." -ForegroundColor Cyan
$tests += Test-Conv $txtFile 'zip'
$tests += Test-Conv $imgFile 'zip'

$resultsPath = Join-Path $env:TEMP 'omni_test_results.txt'
$tests | Format-Table Input, Target, Success, FileCreated, Size, Message -AutoSize | Out-String | Set-Content -Path $resultsPath -Encoding UTF8

$passed = ($tests | Where-Object { $_.Success }).Count
$total = $tests.Count
$failed = $total - $passed

Write-Host "=========================================" -ForegroundColor Green
Write-Host "TEST SUITE SUMMARY: $passed / $total PASSED ($failed FAILED)" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "Results written to $resultsPath" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue

if ($failed -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Yellow
    $tests | Where-Object { -not $_.Success } | Format-Table Input, Target, Message -AutoSize
    exit 1
} else {
    exit 0
}
