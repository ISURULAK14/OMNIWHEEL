param(
    [Parameter(Mandatory=$true)]
    [string]$InputPath,
    [Parameter(Mandatory=$false)]
    [string]$OutputPath,
    [Parameter(Mandatory=$false)]
    [string]$Format = "txt" # txt, docx, clipboard
)

if ([System.Environment]::OSVersion.Version.Major -lt 10) {
    Write-Error "Windows OCR requires Windows 10 Build 10240 or newer."
    [System.Environment]::Exit(2)
}

try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    Add-Type -AssemblyName System.Drawing
    [Windows.Storage.StorageFile, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Globalization.Language, Windows.Globalization, ContentType=WindowsRuntime] | Out-Null
    [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
    [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
} catch {
    Write-Error "WinRT OCR APIs are not available on this platform: $($_.Exception.Message)"
    [System.Environment]::Exit(2)
}

$asTaskOp = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' 
})[0]

$asTaskAction = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' 
})[0]

function AwaitOp($op, $type) {
    $m = $asTaskOp.MakeGenericMethod($type)
    $t = $m.Invoke($null, @($op))
    $t.Wait()
    return $t.Result
}

function AwaitAction($action) {
    $t = $asTaskAction.Invoke($null, @($action))
    $t.Wait()
}

function GetOcrEngine() {
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if (-not $engine) {
        $langs = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
        if ($langs.Count -gt 0) {
            $langObj = New-Object Windows.Globalization.Language($langs[0].LanguageTag)
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($langObj)
        }
    }
    if (-not $engine) { throw "No Windows OCR engine available." }
    return $engine
}

function OcrSoftwareBitmap($softBmp, $engine) {
    $ocrOp = $engine.RecognizeAsync($softBmp)
    $ocrRes = AwaitOp $ocrOp ([Windows.Media.Ocr.OcrResult])
    return $ocrRes.Text
}

function OcrImageFile($imgPath, $engine) {
    $resolved = (Resolve-Path $imgPath).Path
    $fileOp = [Windows.Storage.StorageFile]::GetFileFromPathAsync($resolved)
    $file = AwaitOp $fileOp ([Windows.Storage.StorageFile])

    $streamOp = $file.OpenAsync([Windows.Storage.FileAccessMode]::Read)
    $stream = AwaitOp $streamOp ([Windows.Storage.Streams.IRandomAccessStream])

    $decoderOp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)
    $decoder = AwaitOp $decoderOp ([Windows.Graphics.Imaging.BitmapDecoder])

    $softBmpOp = $decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied)
    $softBmp = AwaitOp $softBmpOp ([Windows.Graphics.Imaging.SoftwareBitmap])

    $text = OcrSoftwareBitmap $softBmp $engine
    $stream.Dispose()
    $softBmp.Dispose()
    return $text
}

function OcrPdfFile($pdfPath, $engine) {
    [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
    [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
    $resolved = (Resolve-Path $pdfPath).Path
    $fileOp = [Windows.Storage.StorageFile]::GetFileFromPathAsync($resolved)
    $file = AwaitOp $fileOp ([Windows.Storage.StorageFile])

    $docOp = [Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)
    $doc = AwaitOp $docOp ([Windows.Data.Pdf.PdfDocument])

    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $doc.PageCount; $i++) {
        $page = $doc.GetPage($i)
        $memStream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream

        # Render at high fidelity (300 DPI equivalent) for perfect OCR text accuracy
        $renderOpts = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $targetWidth = [Math]::Max(2400, [int]($page.Size.Width * 3.0))
        $renderOpts.DestinationWidth = [uint32]$targetWidth

        $renderOp = $page.RenderToStreamAsync($memStream, $renderOpts)
        AwaitAction $renderOp

        $decoderOp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($memStream)
        $decoder = AwaitOp $decoderOp ([Windows.Graphics.Imaging.BitmapDecoder])

        $softBmpOp = $decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied)
        $softBmp = AwaitOp $softBmpOp ([Windows.Graphics.Imaging.SoftwareBitmap])

        $pageText = OcrSoftwareBitmap $softBmp $engine
        if ($pageText) {
            if ($i -gt 0) {
                # Clean Form Feed (ASCII 12) + page break marker
                $sb.AppendLine("`f") | Out-Null
            }
            $sb.AppendLine($pageText) | Out-Null
        }
        $memStream.Dispose()
        $softBmp.Dispose()
    }
    return $sb.ToString().Trim()
}

try {
    if (-not (Test-Path $InputPath)) {
        throw "Input file not found: $InputPath"
    }

    $engine = GetOcrEngine
    $ext = [System.IO.Path]::GetExtension($InputPath).TrimStart('.').ToLower()
    $extractedText = ""

    if ($ext -eq "pdf") {
        $extractedText = OcrPdfFile $InputPath $engine
    } else {
        $extractedText = OcrImageFile $InputPath $engine
    }

    if ($OutputPath) {
        [System.IO.File]::WriteAllText($OutputPath, $extractedText, [System.Text.Encoding]::UTF8)
        Write-Output "SUCCESS:$OutputPath"
    } else {
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Write-Output $extractedText
    }
    [System.Environment]::Exit(0)
} catch {
    Write-Error $_.Exception.Message
    [System.Environment]::Exit(1)
}
