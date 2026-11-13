# OMNIWHEEL — Native Windows Desktop Shell Utility

A 100% native Windows desktop application and background shell extension written in C# / .NET / WPF that integrates directly into Windows File Explorer and the Windows Desktop.

## Architectural Overview

- **App Icon & Visual Branding:** Royal Purple Wheel with crisp white stripes/spokes and central hub.
- **Low-Level Keyboard Hook (`WH_KEYBOARD_LL`):** Global background hook monitoring keyboard events. When the active window is Windows File Explorer (`CabinetWClass`, `ExploreWClass`) or Desktop (`Progman`, `WorkerW`), pressing and holding the assigned hotkey (**`` ` `` (Backtick key)**) summons the OMNIWHEEL radial donut overlay at your cursor.
- **Windows Startup Integration:** Toggleable option in System Tray context menu ("Run at Windows Startup") and Settings window, registered via HKCU Run registry key and Windows Startup folder shortcut.
- **Shell COM Selection Query:** Uses `Shell.Application` / `IShellWindows` / `FolderView` COM interfaces to query the active Explorer window and extract the exact absolute paths of all selected/highlighted files.
- **Hardware-Accelerated WPF Direct2D Overlay:**
  - **Center Hub:** Radial compass displaying file count, format, and instant action/cancel target.
  - **Outer Wedges:** Quick action slices with hover indicators.
  - **Flippable Back Face (Toolbox):** Press **`Tab`** or scroll the **Mouse Wheel** to flip to the toolbox (`Rotate 90°`, `Flip H`, `Flip V`, `Grayscale`, `Invert`, `1:1 Square`, `ZIP Bundle`, `Merge PDF`).
- **Native File Processing Engines:**
  - **Images:** GDI+ / WIC pipeline supporting PNG, JPEG, WEBP, BMP, ICO (multi-size ICO packs: 16, 24, 32, 48, 64, 128, 256), TIFF, and PDF.
  - **Documents:** Text, Markdown, HTML, RTF, and native image-to-PDF stream generator.
  - **Data:** JSON, JSON Lines (JSONL), CSV, TSV, XML, vCard (VCF).
  - **Subtitles:** SRT <-> WebVTT <-> JSON <-> TXT.
  - **Archives:** ZIP extraction and multi-file packaging via `System.IO.Compression`.
  - **Media:** 16-bit PCM WAV, and automatic local FFmpeg CLI bridge.
- **Windows Shell Context Menu (`IContextMenu`):**
  - Registered under `HKCU\Software\Classes\*\shell\OMNIWHEEL`. Right-click any file in Windows Explorer -> **OMNIWHEEL (Donut)**.

## Quick Start & Installation

1. Run **`Install.bat`** (or compile with **`Build.bat`**).
2. The application will start and sit silently in your Windows System Tray with a royal purple wheel icon.
3. Open Windows File Explorer or go to your Desktop, select any file(s), and press and hold **`` ` `` (Backtick)**.
4. Release on any action to convert, or drag to center hub to cancel.
5. Right-click the royal purple wheel icon in the system tray to toggle **"Run at Windows Startup"** or configure settings.
