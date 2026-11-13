$filePath = "E:\ANTIGRAVITY\OMNIWHEEL\OMNIWHEEL_Setup.exe"
$bytes = [System.IO.File]::ReadAllBytes($filePath)
$peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
# OptionalHeader starts at peOffset + 24 (0x18).
# DllCharacteristics is at offset 70 (0x46) within OptionalHeader for PE32+, or offset 70 in standard PE.
$magic = [System.BitConverter]::ToUInt16($bytes, $peOffset + 24)
$dllCharOffset = if ($magic -eq 0x020B) { $peOffset + 24 + 70 } else { $peOffset + 24 + 70 }
$dllChars = [System.BitConverter]::ToUInt16($bytes, $dllCharOffset)

Write-Host ("PE Magic: 0x{0:X4}" -f $magic)
Write-Host ("DllCharacteristics: 0x{0:X4}" -f $dllChars)
Write-Host (" [PASS] High-Entropy ASLR (0x0020): " + (($dllChars -band 0x0020) -ne 0))
Write-Host (" [PASS] DynamicBase ASLR  (0x0040): " + (($dllChars -band 0x0040) -ne 0))
Write-Host (" [PASS] NX Compat DEP     (0x0100): " + (($dllChars -band 0x0100) -ne 0))
Write-Host (" [PASS] TerminalServerAware (0x8000): " + (($dllChars -band 0x8000) -ne 0))
