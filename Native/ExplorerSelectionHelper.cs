using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LocalConvertNative.Shell
{
    /// <summary>
    /// Communicates with Windows Shell via IShellWindows / IServiceProvider / IShellView
    /// to retrieve the exact absolute paths of files currently highlighted/selected in File Explorer or Desktop.
    /// Compatible with Windows 10, Windows 11 (including Tabbed Explorer), and Desktop.
    /// </summary>
    public static class ExplorerSelectionHelper
    {
        private const uint GA_ROOT = 2;
        private const uint GA_ROOTOWNER = 3;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public static List<string> GetSelectedFilePaths()
        {
            if (System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA)
            {
                try
                {
                    return GetSelectedFilePathsInternal();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ExplorerSelectionHelper] Direct STA call error: " + ex.Message);
                    return new List<string>();
                }
            }

            var selectedFiles = new List<string>();
            var thread = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                try
                {
                    selectedFiles = GetSelectedFilePathsInternal();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ExplorerSelectionHelper] STA Thread error: " + ex.Message);
                }
            }));

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            // 1200ms timeout to allow Windows Shell COM to enumerate on busy systems
            thread.Join(1200);

            return selectedFiles;
        }

        private static List<string> GetSelectedFilePathsInternal()
        {
            var selectedFiles = new List<string>();

            IntPtr fgHwnd = GetForegroundWindow();
            IntPtr rootFgHwnd = fgHwnd != IntPtr.Zero ? GetAncestor(fgHwnd, GA_ROOT) : IntPtr.Zero;
            if (rootFgHwnd == IntPtr.Zero) rootFgHwnd = fgHwnd;

            POINT mousePt;
            GetCursorPos(out mousePt);
            IntPtr ptHwnd = WindowFromPoint(mousePt);
            IntPtr rootPtHwnd = ptHwnd != IntPtr.Zero ? GetAncestor(ptHwnd, GA_ROOT) : IntPtr.Zero;
            if (rootPtHwnd == IntPtr.Zero) rootPtHwnd = ptHwnd;

            dynamic shell = null;
            dynamic windows = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return selectedFiles;

                shell = Activator.CreateInstance(shellType);
                windows = shell.Windows();
                int count = 0;
                try { count = (int)windows.Count; } catch { count = 0; }

                // Step 1: If cursor or foreground is on Desktop, query Desktop first!
                if (IsDesktopWindow(ptHwnd) || IsDesktopWindow(fgHwnd))
                {
                    var desktopFiles = GetDesktopSelectedFiles(windows);
                    if (desktopFiles.Count > 0)
                    {
                        return desktopFiles;
                    }
                }

                // Step 2: Check target Explorer window by HWND
                // Priority: Window directly under mouse cursor, then foreground window
                IntPtr[] targetHwnds = new IntPtr[] { ptHwnd, fgHwnd };
                foreach (IntPtr target in targetHwnds)
                {
                    if (target == IntPtr.Zero) continue;
                    IntPtr targetRoot = GetAncestor(target, GA_ROOT);
                    if (targetRoot == IntPtr.Zero) targetRoot = target;

                    for (int i = 0; i < count; i++)
                    {
                        dynamic window = null;
                        try
                        {
                            window = windows.Item(i);
                            if (window == null) continue;

                            long winHwndVal = 0;
                            try { winHwndVal = (long)window.HWND; } catch { continue; }
                            IntPtr winHwnd = (IntPtr)winHwndVal;
                            IntPtr winRoot = GetAncestor(winHwnd, GA_ROOT);
                            if (winRoot == IntPtr.Zero) winRoot = winHwnd;

                            bool isMatch = false;
                            if (winHwnd == target || winRoot == targetRoot || winHwnd == targetRoot || winRoot == target)
                            {
                                isMatch = true;
                            }
                            else if (IsChild(winHwnd, target) || IsChild(target, winHwnd) ||
                                     IsChild(winRoot, target) || IsChild(targetRoot, winHwnd))
                            {
                                isMatch = true;
                            }

                            if (isMatch)
                            {
                                var files = ExtractFilesFromWindow(window);
                                if (files.Count > 0)
                                {
                                    return files;
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            if (window != null)
                            {
                                try { Marshal.ReleaseComObject(window); } catch { }
                            }
                        }
                    }
                }

                // Step 3: Desktop fallback (in case cursor was slightly off or on secondary monitor)
                var fallbackDesktopFiles = GetDesktopSelectedFiles(windows);
                if (fallbackDesktopFiles.Count > 0)
                {
                    return fallbackDesktopFiles;
                }

                // Step 4: Fallback - Search open Explorer windows for any window with selected items
                for (int i = 0; i < count; i++)
                {
                    dynamic window = null;
                    try
                    {
                        window = windows.Item(i);
                        if (window == null) continue;

                        var files = ExtractFilesFromWindow(window);
                        if (files.Count > 0)
                        {
                            return files;
                        }
                    }
                    catch { }
                    finally
                    {
                        if (window != null)
                        {
                            try { Marshal.ReleaseComObject(window); } catch { }
                        }
                    }
                }

                // Step 5: Fallback to Windows Clipboard (e.g. if user pressed Ctrl+C on a file)
                try
                {
                    if (Clipboard.ContainsFileDropList())
                    {
                        var dropList = Clipboard.GetFileDropList();
                        foreach (string p in dropList)
                        {
                            if (File.Exists(p) || Directory.Exists(p))
                            {
                                selectedFiles.Add(p);
                            }
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ExplorerSelectionHelper] Error: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (windows != null) Marshal.ReleaseComObject(windows);
                    if (shell != null) Marshal.ReleaseComObject(shell);
                }
                catch { }
            }

            return selectedFiles;
        }

        private static bool IsDesktopWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            IntPtr shellWin = GetShellWindow();
            if (hwnd == shellWin) return true;

            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;
            if (root == shellWin) return true;

            StringBuilder sb = new StringBuilder(256);
            GetClassName(root, sb, 256);
            string rootCls = sb.ToString();
            if (rootCls == "Progman" || rootCls == "WorkerW") return true;

            GetClassName(hwnd, sb, 256);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView") return true;

            return false;
        }

        private static List<string> GetDesktopSelectedFiles(dynamic windows)
        {
            var files = new List<string>();
            dynamic disp = null;
            try
            {
                int desktopHwnd = 0;
                // FindWindowSW(0, null, SWC_DESKTOP=8, out hwnd, SWFO_NEEDDISPATCH=1)
                disp = windows.FindWindowSW(0, null, 8, out desktopHwnd, 1);
                if (disp != null)
                {
                    files = ExtractFilesFromWindow(disp);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ExplorerSelectionHelper] FindWindowSW error: " + ex.Message);
            }
            finally
            {
                if (disp != null)
                {
                    try { Marshal.ReleaseComObject(disp); } catch { }
                }
            }
            return files;
        }

        private static List<string> ExtractFilesFromWindow(dynamic window)
        {
            var files = new List<string>();
            dynamic doc = null;
            dynamic selectedItems = null;
            dynamic fi = null;
            try
            {
                doc = window.Document;
                if (doc == null) return files;

                selectedItems = doc.SelectedItems();
                int selCount = 0;
                try { if (selectedItems != null) selCount = (int)selectedItems.Count; } catch { selCount = 0; }

                for (int j = 0; j < selCount; j++)
                {
                    dynamic item = null;
                    try
                    {
                        item = selectedItems.Item(j);
                        if (item == null) continue;

                        string path = (string)item.Path;
                        if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                        {
                            files.Add(path);
                        }
                    }
                    catch { }
                    finally
                    {
                        if (item != null)
                        {
                            try { Marshal.ReleaseComObject(item); } catch { }
                        }
                    }
                }

                // If SelectedItems was empty, check FocusedItem (single click / drag race condition)
                if (files.Count == 0)
                {
                    try
                    {
                        fi = doc.FocusedItem;
                        if (fi != null)
                        {
                            string p = (string)fi.Path;
                            if (!string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)))
                            {
                                files.Add(p);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                if (fi != null) { try { Marshal.ReleaseComObject(fi); } catch { } }
                if (selectedItems != null) { try { Marshal.ReleaseComObject(selectedItems); } catch { } }
                if (doc != null) { try { Marshal.ReleaseComObject(doc); } catch { } }
            }
            return files;
        }
    }
}
