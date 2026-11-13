# OMNIWHEEL Native Static Code Audit & Security Analysis Report

**Workspace:** `E:\ANTIGRAVITY\OMNIWHEEL\Native`  
**Date:** 2026-11-05  
**Auditor:** STATIC AUDIT & SECURITY AGENT  
**Automated Verification Harness:** `E:\ANTIGRAVITY\OMNIWHEEL\Native\tests\harness_static_security.ps1`

---

## 1. Executive Summary

Static code analysis was conducted across all 12 primary source files in `E:\ANTIGRAVITY\OMNIWHEEL\Native`:
- `FileConversionEngine.cs` (4,795 lines)
- `Program.cs` (853 lines)
- `RadialDonutWindow.cs` (1,315 lines)
- `QuickLookWindow.cs` (1,793 lines)
- `ProgressFeedbackWindow.cs` (310 lines)
- `KeyboardHook.cs` (613 lines)
- `ExplorerSelectionHelper.cs` (363 lines)
- `AppSettings.cs` (233 lines)
- `Logger.cs` (116 lines)
- `CurdTheme.cs` (365 lines)
- `WindowsOcrEngine.ps1` (134 lines)
- `Installer\OMNIWHEEL_Setup.cs` (2,199 lines)

**Overall Score:** 10 Passed / 7 Actionable Findings (58.8% baseline security compliance).

---

## 2. Security & Hardening Controls Verified (Passed)

| Check ID | Security Control | Target File & Lines | Implementation & Defense Verification |
|:---|:---|:---|:---|
| **RES-01** | Win32 GDI HICON Handle Cleanup | `Program.cs:509-520` | `bmp.GetHicon()` paired with `DestroyIcon(hIcon)` in `finally` block; icon cloned to managed memory. No GDI handle leak. |
| **RES-02** | GDI HBITMAP Handle Cleanup | `QuickLookWindow.cs:709-718` | `gdiBmp.GetHbitmap()` released via P/Invoke `DeleteObject(hBmp)` in `finally` block. |
| **RES-03** | Deterministic Stream & Zip Disposal | `FileConversionEngine.cs:2260, 2503, 2564, 3510` | All `FileStream`, `MemoryStream`, `DeflateStream`, and `ZipArchive` instances wrapped in `using()` scopes. |
| **CONC-01** | Zero Synchronous Dispatcher Deadlocks | All UI Windows (`Program.cs`, `RadialDonutWindow.cs`, `ProgressFeedbackWindow.cs`) | Background worker threads exclusively dispatch UI updates via asynchronous `Dispatcher.BeginInvoke`. No `Dispatcher.Invoke` deadlocks. |
| **CONC-04** | Thread-Safe Log File Synchronization | `Logger.cs:93-108` | Log entries and file rotation protected by `lock (_lock)` monitor. |
| **SEC-01** | Zip-Slip Path Traversal Mitigation | `FileConversionEngine.cs:2566-2576` | Entry extraction target path normalized and validated: `fullPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)`. Directory traversal blocked. |
| **SEC-02** | CLI Argument Injection Sanitization | `FileConversionEngine.cs:4334-4342` | `SanitizePathForCommandLine` rejects quotes `"` and `\r\n\0`, strips trailing backslashes with `.TrimEnd('\\')` to prevent escaping closing quote in Windows argument parser (`CommandLineToArgvW`). |
| **SEC-06** | DLL Hijacking & Binary Planting Defenses | `Program.cs:90-91`, `OMNIWHEEL_Setup.cs:45-46` | Immediate process entrypoint call to `SetDllDirectory("")` (removes CWD from search order) and `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS \| SYSTEM32 \| APPLICATION_DIR)`. |
| **SEC-07** | Cryptographic Self-Integrity Check | `Program.cs:46-78, 94-104` | Validates Authenticode certificate subject `ISURU GUNASEKERA` and SHA-1 thumbprint `601AF83746DF7AA5CFCFC83F8B6FA2FA03883B05`. Prevents execution of tampered binaries. |
| **SEC-08** | XML External Entity (XXE) Injection Defense | `FileConversionEngine.cs:4775-4793` | `CreateSafeXmlReader` enforces `DtdProcessing = DtdProcessing.Prohibit` and `XmlResolver = null`. Prevents SSRF and billion-laughs DoS. |

---

## 3. Vulnerabilities & Code Defects (Detailed Findings & Surgical Patches)

### Finding 1 [HIGH]: COM RCW Leaks in Office Interop (Zombie WINWORD.EXE / EXCEL.EXE)
- **Check IDs:** `RES-04`, `RES-05`
- **Files & Lines:**
  - `FileConversionEngine.cs:1897` (Word legacy DOC fallback)
  - `FileConversionEngine.cs:2137` (Word PDF reflow)
  - `FileConversionEngine.cs:3344-3375` (Excel automation)

#### Vulnerable Pattern:
1. `FileConversionEngine.cs:1897` and `2137`:
   ```csharp
   dynamic doc = wordApp.Documents.Open(inputPath, ReadOnly: true);
   ```
   Accessing `wordApp.Documents.Open(...)` creates an intermediate RCW for the `Documents` collection. Because it is never assigned to a variable, it is never released via `Marshal.FinalReleaseComObject`.
2. Single `Marshal.ReleaseComObject(...)` is called instead of `Marshal.FinalReleaseComObject(...)`. In dynamic COM dispatch, reference counts can exceed 1, leaving the COM server running.
3. `FileConversionEngine.cs:3344`:
   ```csharp
   dynamic workbooks = excelApp.Workbooks;
   dynamic wb = workbooks.Open(inputPath, ReadOnly: true);
   ```
   `workbooks` is assigned at line 3344, but the `finally` block only releases `wb` and `excelApp`. `workbooks` RCW is leaked.
4. Missing post-cleanup Garbage Collection: Office COM servers require `GC.Collect()` and `GC.WaitForPendingFinalizers()` to release out-of-process COM servers.

#### Surgical Remediation:
In `FileConversionEngine.cs` (Word conversion blocks):
```csharp
dynamic documents = null;
dynamic doc = null;
try
{
    documents = wordApp.Documents;
    doc = documents.Open(inputPath, ReadOnly: true);
    ...
}
finally
{
    if (doc != null) { try { doc.Close(false); Marshal.FinalReleaseComObject(doc); } catch { } }
    if (documents != null) { try { Marshal.FinalReleaseComObject(documents); } catch { } }
    if (wordApp != null) { try { wordApp.Quit(); Marshal.FinalReleaseComObject(wordApp); } catch { } }
    GC.Collect();
    GC.WaitForPendingFinalizers();
}
```
In `FileConversionEngine.cs:3367-3375` (Excel conversion block):
```csharp
finally
{
    if (wb != null) { try { wb.Close(false); Marshal.FinalReleaseComObject(wb); } catch { } }
    if (workbooks != null) { try { Marshal.FinalReleaseComObject(workbooks); } catch { } }
    if (excelApp != null) { try { excelApp.Quit(); Marshal.FinalReleaseComObject(excelApp); } catch { } }
    GC.Collect();
    GC.WaitForPendingFinalizers();
}
```

---

### Finding 2 [MEDIUM]: COM RCW Leak in WScript.Shell Shortcut Creation
- **Check ID:** `RES-06`
- **File & Lines:** `Installer\OMNIWHEEL_Setup.cs:444-456`

#### Vulnerable Pattern:
```csharp
Type shellType = Type.GetTypeFromProgID("WScript.Shell");
if (shellType != null)
{
    dynamic shell = Activator.CreateInstance(shellType);
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = targetPath;
    ...
    shortcut.Save();
}
```
Neither `shortcut` nor `shell` COM object is released via `Marshal.FinalReleaseComObject`.

#### Surgical Remediation:
```csharp
dynamic shell = null;
dynamic shortcut = null;
try
{
    shell = Activator.CreateInstance(shellType);
    shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = targetPath;
    shortcut.WorkingDirectory = workingDir;
    shortcut.Description = description;
    shortcut.IconLocation = targetPath + ",0";
    shortcut.Save();
}
finally
{
    if (shortcut != null) { try { Marshal.FinalReleaseComObject(shortcut); } catch { } }
    if (shell != null) { try { Marshal.FinalReleaseComObject(shell); } catch { } }
}
```

---

### Finding 3 [MEDIUM]: Unhandled 7-Zip CLI Timeout & Process Orphan
- **Check ID:** `CONC-02`
- **File & Lines:** `FileConversionEngine.cs:2614-2615`

#### Vulnerable Pattern:
```csharp
using (var p = Process.Start(psi7))
{
    p.WaitForExit(60000);
    if (p.ExitCode == 0) // Throws InvalidOperationException if WaitForExit times out!
    {
        return new ConversionResult { Success = true, OutputPath = outDir, Message = "Extracted to: " + Path.GetFileName(outDir) };
    }
}
```
`p.WaitForExit(60000)` returns a boolean. If 7-Zip hangs, times out, or exceeds 60s:
1. `p.ExitCode` throws `System.InvalidOperationException: Process must exit before requested information can be determined.`
2. The 7z child process is never killed and remains running as a zombie process.

#### Surgical Remediation:
```csharp
using (var p = Process.Start(psi7))
{
    if (!p.WaitForExit(60000))
    {
        try { p.Kill(); } catch { }
        return new ConversionResult { Success = false, Message = "7-Zip extraction timed out after 60s" };
    }
    if (p.ExitCode == 0)
    {
        return new ConversionResult { Success = true, OutputPath = outDir, Message = "Extracted to: " + Path.GetFileName(outDir) };
    }
}
```

---

### Finding 4 [LOW-MEDIUM]: Concurrency Race Condition on Progress Overlay State
- **Check ID:** `CONC-03`
- **File & Lines:** `RadialDonutWindow.cs:811-852`

#### Vulnerable Pattern:
```csharp
int currentPct = (int)segStart;
...
using (var ticker = new System.Threading.Timer(delegate(object state)
{
    if (fileFinished) return;
    if (currentPct < (int)(segEnd - 3))
    {
        currentPct = Math.Min((int)(segEnd - 3), currentPct + 2); // Thread 1 writes
        Dispatcher.BeginInvoke(...);
    }
}, null, 120, 120))
{
    var res = FileConversionEngine.Convert(..., delegate(int enginePct, string msg)
    {
        int mapped = (int)(segStart + (enginePct / 100.0) * (segEnd - segStart));
        if (mapped > currentPct)
        {
            currentPct = mapped; // Thread 2 writes
            Dispatcher.BeginInvoke(...);
        }
    });
    ...
}
```
`currentPct` is a captured local variable read and mutated concurrently by the `System.Threading.Timer` thread pool worker and the conversion progress callback thread without memory barrier or lock, leading to data race and non-monotonic progress percentage.

#### Surgical Remediation:
Synchronize read-modify-write via a dedicated lock object or `Interlocked.Exchange`.

---

### Finding 5 [MEDIUM]: Bare Binary Execution in `powershell.exe` & `cmd.exe`
- **Check IDs:** `SEC-03`, `SEC-04`
- **Files & Lines:**
  - `FileConversionEngine.cs:4586`: `RunProcessSafely("powershell.exe", args, 60000);`
  - `OMNIWHEEL_Setup.cs:347, 2148`: `FileName = "cmd.exe"`

#### Vulnerable Pattern:
Passing bare executable names (`powershell.exe`, `cmd.exe`) causes `CreateProcess` to traverse the default Windows search order. While `SetDllDirectory("")` protects DLL loading, bare executable names can be vulnerable to binary planting if an executable is placed earlier in the search path.

#### Surgical Remediation:
1. In `FileConversionEngine.cs:4586`:
   ```csharp
   string psPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
   RunProcessSafely(File.Exists(psPath) ? psPath : "powershell.exe", args, 60000);
   ```
2. In `OMNIWHEEL_Setup.cs:347, 2148`:
   ```csharp
   FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
   ```

---

### Finding 6 [MEDIUM]: Unverified External Binary Copying in Setup Fallback
- **Check ID:** `SEC-05`
- **File & Lines:** `Installer\OMNIWHEEL_Setup.cs:476-480, 534-539`

#### Vulnerable Pattern:
```csharp
string localFfmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
if (File.Exists(localFfmpeg))
{
    File.Copy(localFfmpeg, Path.Combine(targetDir, "ffmpeg.exe"), true);
}
```
When `OMNIWHEEL_Setup.exe` runs from a shared folder (e.g., `Downloads`), any untrusted `ffmpeg.exe` sitting next to setup is copied directly into `%LocalAppData%\OMNIWHEEL\ffmpeg.exe` without Authenticode or hash validation.

#### Surgical Remediation:
Validate Authenticode signature or hardcoded SHA-256 hash before copying external binaries:
```csharp
if (File.Exists(localFfmpeg) && VerifyBinaryHash(localFfmpeg))
{
    File.Copy(localFfmpeg, Path.Combine(targetDir, "ffmpeg.exe"), true);
}
```

---

### Finding 7 [LOW]: Ephemeral Brush & Pen Allocations in WPF Animation Render Loop
- **File & Lines:** `ProgressFeedbackWindow.cs:198-280`

#### Vulnerable Pattern:
`OnRender(DrawingContext dc)` is invoked at 60 FPS (every 16ms) during progress animation. 8 Pens/Brushes, a `Typeface`, and `FormattedText` are allocated on every tick. Although `.Freeze()` is called, heap allocations create transient Gen0 GC churn.

#### Surgical Remediation:
Declare frozen brushes and pens as `private static readonly` members of `ProgressFeedbackWindow`.

---

## 4. Verification & Regression Testing

The automated test harness `tests\harness_static_security.ps1` runs static pattern evaluation across the codebase:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\ANTIGRAVITY\OMNIWHEEL\Native\tests\harness_static_security.ps1"
```
Output:
- Detailed PASS/WARN per control
- Exact file paths and line ranges
- Specific surgical patch recommendations
