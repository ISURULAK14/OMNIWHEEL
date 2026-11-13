try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    $langs = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
    Write-Host "Available OCR languages: " ($langs | ForEach-Object { $_.LanguageTag })
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    Write-Host "OCR Engine created: " ($engine -ne $null)
    if ($engine) {
        Write-Host "Engine Language: " $engine.RecognizerLanguage.LanguageTag
    }
} catch {
    Write-Host "Error: " $_.Exception.ToString()
}
