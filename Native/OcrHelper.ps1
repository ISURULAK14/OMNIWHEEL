param(
    [Parameter(Mandatory=$false)]
    [string]$ImagePath,
    [Parameter(Mandatory=$false)]
    [string]$OutputPath
)

Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Drawing
[Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.StorageFile, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null

$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' 
})[0]

function AwaitOperation($op, $type) {
    $m = $asTask.MakeGenericMethod($type)
    $task = $m.Invoke($null, @($op))
    $task.Wait()
    return $task.Result
}

function RunOcrOnImage($path) {
    $resolved = (Resolve-Path $path).Path
    $fileOp = [Windows.Storage.StorageFile]::GetFileFromPathAsync($resolved)
    $file = AwaitOperation $fileOp ([Windows.Storage.StorageFile])

    $streamOp = $file.OpenAsync([Windows.Storage.FileAccessMode]::Read)
    $stream = AwaitOperation $streamOp ([Windows.Storage.Streams.IRandomAccessStream])

    $decoderOp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)
    $decoder = AwaitOperation $decoderOp ([Windows.Graphics.Imaging.BitmapDecoder])

    $softBmpOp = $decoder.GetSoftwareBitmapAsync()
    $softBmp = AwaitOperation $softBmpOp ([Windows.Graphics.Imaging.SoftwareBitmap])

    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if (-not $engine) {
        $langs = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
        if ($langs.Count -gt 0) {
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($langs[0])
        }
    }
    if (-not $engine) {
        throw "No Windows OCR engine installed on system."
    }

    $ocrOp = $engine.RecognizeAsync($softBmp)
    $ocrResult = AwaitOperation $ocrOp ([Windows.Media.Ocr.OcrResult])
    return $ocrResult.Text
}

if ($ImagePath -and (Test-Path $ImagePath)) {
    $text = RunOcrOnImage $ImagePath
    if ($OutputPath) {
        [System.IO.File]::WriteAllText($OutputPath, $text, [System.Text.Encoding]::UTF8)
    } else {
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Write-Output $text
    }
}
