using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LocalConvertNative.Shell
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEWHEEL = 0x020A;

        private const int VK_SPACE = 0x20;
        private const int VK_D = 0x44;
        private const int VK_TAB = 0x09;
        private const int VK_OEM_3 = 0xC0; // '`' / '~' key (Backtick / Grave accent)
        private const uint GA_ROOT = 2;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelProc _keyboardProc;
        private LowLevelProc _mouseProc;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;

        private bool _isKeyHeld = false;
        private int _heldVkCode = 0;
        private bool _isCtrlClick = false;
        private bool _isMouseWheelActive = false;
        private POINT _dragStart;

        public event Action<int, int> OnKeyDownTriggered;
        public event Action OnKeyUpTriggered;
        public event Action<int, int> OnMouseMoveWhileActive;
        public event Action OnTabPressed;

        // Spacebar Peek / QuickLook Events
        public event Action<System.Collections.Generic.List<string>> OnSpacePeekTriggered;
        public event Action OnPeekCloseTriggered;
        public event Action<int> OnPeekNavigateTriggered; // -1 = Prev, +1 = Next

        public bool OnlyWhenExplorerActive { get; set; }
        public bool IsKeyHeld { get { return _isKeyHeld; } }
        public Func<bool> IsWheelVisibleChecker { get; set; }
        public Func<bool> IsPeekVisibleChecker { get; set; }
        public string PrimaryHotkey { get; set; }
        public bool EnableMouseWheelFlip { get; set; }
        public bool EnableKeyFlip { get; set; }
        public bool EnableSpacePeek { get; set; }

        public KeyboardHook()
        {
            OnlyWhenExplorerActive = true;
            PrimaryHotkey = "Backtick";
            EnableMouseWheelFlip = true;
            EnableKeyFlip = true;
            EnableSpacePeek = true;

            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                if (hMod == IntPtr.Zero)
                {
                    hMod = GetModuleHandle(null);
                }
                _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
                _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
            }
        }

        private bool IsMatchingHotkey(int vkCode, bool isCtrl, bool isAlt, bool isShift)
        {
            string hk = PrimaryHotkey ?? "Backtick";
            if (hk.Equals("CtrlSpace", StringComparison.OrdinalIgnoreCase))
            {
                return vkCode == 0x20 && isCtrl && !isAlt;
            }
            if (hk.Equals("AltD", StringComparison.OrdinalIgnoreCase))
            {
                return vkCode == 0x44 && isAlt && !isCtrl;
            }
            if (hk.Equals("F4", StringComparison.OrdinalIgnoreCase))
            {
                return vkCode == 0x73 && !isAlt && !isCtrl;
            }
            // Default: Backtick / Tilde (` / ~) -> VK_OEM_3 (0xC0)
            return vkCode == VK_OEM_3 && !isAlt && !isCtrl;
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int vkCode = Marshal.ReadInt32(lParam);

                bool isAltPressed = (GetKeyState(0x12) & 0x8000) != 0; // VK_MENU (Alt)
                bool isCtrlPressed = (GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
                bool isShiftPressed = (GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT

                bool isWheelOpen = (IsWheelVisibleChecker != null && IsWheelVisibleChecker());
                bool isWheelActive = _isKeyHeld || _isMouseWheelActive || isWheelOpen;
                bool isPeekOpen = (IsPeekVisibleChecker != null && IsPeekVisibleChecker());

                // KEY DOWN EVENT
                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    // 0. If QuickLook / Peek window is currently OPEN:
                    if (isPeekOpen)
                    {
                        // Space bar or Escape key closes the preview
                        if (vkCode == VK_SPACE || vkCode == 0x1B)
                        {
                            if (OnPeekCloseTriggered != null)
                            {
                                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnPeekCloseTriggered(); }));
                            }
                            return (IntPtr)1;
                        }

                        // Arrow keys flip through files sequentially
                        if (vkCode == 0x25 || vkCode == 0x26) // Left (0x25) or Up (0x26) -> Previous file
                        {
                            if (OnPeekNavigateTriggered != null)
                            {
                                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnPeekNavigateTriggered(-1); }));
                            }
                            return (IntPtr)1;
                        }
                        if (vkCode == 0x27 || vkCode == 0x28) // Right (0x27) or Down (0x28) -> Next file
                        {
                            if (OnPeekNavigateTriggered != null)
                            {
                                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnPeekNavigateTriggered(1); }));
                            }
                            return (IntPtr)1;
                        }
                    }

                    // 1. If wheel is ALREADY ACTIVE OR OPEN
                    if (isWheelActive)
                    {
                        // Auto-repeat suppression: if user is holding this key, ignore repeated WM_KEYDOWN
                        if (_isKeyHeld && vkCode == _heldVkCode)
                        {
                            return (IntPtr)1;
                        }

                        // Escape cancels/closes
                        if (vkCode == 0x1B)
                        {
                            _isKeyHeld = false;
                            _heldVkCode = 0;
                            if (OnKeyUpTriggered != null)
                            {
                                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnKeyUpTriggered(); }));
                            }
                            return (IntPtr)1;
                        }

                        // Toggle close if hotkey pressed again while wheel is open
                        if (isWheelOpen && IsMatchingHotkey(vkCode, isCtrlPressed, isAltPressed, isShiftPressed) && !_isKeyHeld)
                        {
                            if (OnKeyUpTriggered != null)
                            {
                                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnKeyUpTriggered(); }));
                            }
                            return (IntPtr)1;
                        }

                        // Flipping layers (Secondary wheels):
                        // While wheel is open/active -> Tab, Space, or F flips layers!
                        if (EnableKeyFlip)
                        {
                            bool isFlipKey = (vkCode == VK_TAB) ||
                                             (vkCode == VK_SPACE) ||
                                             (vkCode == 0x46); // 'F'

                            if (isFlipKey)
                            {
                                if (OnTabPressed != null)
                                {
                                    ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnTabPressed(); }));
                                }
                                return (IntPtr)1;
                            }
                        }
                    }
                    // 2. Wheel is NOT ACTIVE: Triggerable by configured hotkey
                    else
                    {
                        bool isTargetKey = IsMatchingHotkey(vkCode, isCtrlPressed, isAltPressed, isShiftPressed);

                        if (isTargetKey)
                        {
                            bool allowTrigger = true;
                            if (OnlyWhenExplorerActive)
                            {
                                allowTrigger = IsExplorerOrDesktopActive();
                            }

                            if (allowTrigger)
                            {
                                _isKeyHeld = true;
                                _heldVkCode = vkCode;
                                POINT pt;
                                GetCursorPos(out pt);

                                if (OnKeyDownTriggered != null)
                                {
                                    int px = pt.X;
                                    int py = pt.Y;
                                    ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                                    {
                                        if (OnKeyDownTriggered != null) OnKeyDownTriggered(px, py);
                                    }));
                                }

                                return (IntPtr)1;
                            }
                        }
                        // 3. Spacebar QuickLook / Peek Activation (When File Explorer or Desktop holds focus)
                        else if (EnableSpacePeek && vkCode == VK_SPACE && !isCtrlPressed && !isAltPressed && !isShiftPressed)
                        {
                            bool isExplorer = IsExplorerOrDesktopActive();
                            if (isExplorer)
                            {
                                POINT pt;
                                GetCursorPos(out pt);
                                IntPtr ptHwnd = WindowFromPoint(pt);
                                if (!IsEditingTextInExplorer(ptHwnd))
                                {
                                    ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                                    {
                                        var files = ExplorerSelectionHelper.GetSelectedFilePaths();
                                        if (files != null && files.Count > 0)
                                        {
                                            if (OnSpacePeekTriggered != null)
                                            {
                                                OnSpacePeekTriggered(files);
                                            }
                                        }
                                    }));
                                    return (IntPtr)1; // Suppress default Space action in Explorer
                                }
                            }
                        }
                    }
                }
                // KEY UP EVENT (RELEASED)
                else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                {
                    if (_isKeyHeld && (vkCode == _heldVkCode || IsMatchingHotkey(vkCode, isCtrlPressed, isAltPressed, isShiftPressed)))
                    {
                        _isKeyHeld = false;
                        _heldVkCode = 0;

                        if (OnKeyUpTriggered != null)
                        {
                            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                            {
                                if (OnKeyUpTriggered != null) OnKeyUpTriggered();
                            }));
                        }

                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[KeyboardHookCallback] Error: " + ex.Message);
            }
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                int msg = (int)wParam;
                bool isWheelOpen = (IsWheelVisibleChecker != null && IsWheelVisibleChecker());

                // Ultra-fast exit: if not tracking drag/wheel/key and wheel not open, ignore mouse moves immediately
                if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP && msg != WM_MOUSEWHEEL && !_isCtrlClick && !_isMouseWheelActive && !_isKeyHeld && !isWheelOpen)
                {
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }

                MSLLHOOKSTRUCT mouseData = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool isCtrlPressed = (GetKeyState(0x11) & 0x8000) != 0;

                // 1. Mouse Left Button Down
                if (msg == WM_LBUTTONDOWN)
                {
                    if (isCtrlPressed && IsExplorerOrDesktopActive(mouseData.pt))
                    {
                        _isCtrlClick = true;
                        _dragStart = mouseData.pt;
                        _isMouseWheelActive = false;
                    }
                    else
                    {
                        _isCtrlClick = false;
                        _isMouseWheelActive = false;
                    }
                }
                // 2. Mouse Move
                else if (msg == WM_MOUSEMOVE)
                {
                    if (_isCtrlClick && !_isMouseWheelActive)
                    {
                        bool isLButtonDown = (GetKeyState(0x01) & 0x8000) != 0;
                        if (isCtrlPressed && isLButtonDown)
                        {
                            double dx = mouseData.pt.X - _dragStart.X;
                            double dy = mouseData.pt.Y - _dragStart.Y;
                            if ((dx * dx + dy * dy) > 64) // > 8 pixels
                            {
                                // Drag gesture with Ctrl held triggered!
                                _isMouseWheelActive = true;
                                int px = mouseData.pt.X;
                                int py = mouseData.pt.Y;
                                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                                {
                                    if (OnKeyDownTriggered != null) OnKeyDownTriggered(px, py);
                                }));
                            }
                        }
                        else
                        {
                            _isCtrlClick = false;
                        }
                    }
                    else if (_isMouseWheelActive || _isKeyHeld || isWheelOpen)
                    {
                        int px = mouseData.pt.X;
                        int py = mouseData.pt.Y;
                        if (OnMouseMoveWhileActive != null)
                        {
                            OnMouseMoveWhileActive(px, py);
                        }
                    }
                }
                // 3. Mouse Scroll Wheel (Flip layers)
                else if (msg == WM_MOUSEWHEEL)
                {
                    if (EnableMouseWheelFlip && (_isKeyHeld || _isMouseWheelActive || isWheelOpen))
                    {
                        if (OnTabPressed != null)
                        {
                            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate { OnTabPressed(); }));
                        }
                        return (IntPtr)1; // Suppress background Explorer scrolling
                    }
                }
                // 4. Mouse Left Button Up (Drop)
                else if (msg == WM_LBUTTONUP)
                {
                    if (_isMouseWheelActive)
                    {
                        _isMouseWheelActive = false;
                        _isCtrlClick = false;

                        if (OnKeyUpTriggered != null)
                        {
                            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                            {
                                if (OnKeyUpTriggered != null) OnKeyUpTriggered();
                            }));
                        }

                        return (IntPtr)1;
                    }
                    _isCtrlClick = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MouseHookCallback] Error: " + ex.Message);
            }
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

        public void ResetState()
        {
            _isKeyHeld = false;
            _isCtrlClick = false;
            _isMouseWheelActive = false;
        }

        private bool IsExplorerOrDesktopActive(POINT? mousePt = null)
        {
            IntPtr fg = GetForegroundWindow();
            if (IsEditingTextInExplorer(fg)) return false;
            if (IsExplorerProcess(fg) || IsFileExplorerOrDesktop(fg)) return true;

            POINT pt;
            if (mousePt.HasValue) pt = mousePt.Value;
            else GetCursorPos(out pt);

            IntPtr ptHwnd = WindowFromPoint(pt);
            if (IsEditingTextInExplorer(ptHwnd)) return false;
            if (IsExplorerProcess(ptHwnd) || IsFileExplorerOrDesktop(ptHwnd)) return true;

            return false;
        }

        private bool IsExplorerProcess(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0)
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private bool IsEditingTextInExplorer(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                uint processId;
                uint threadId = GetWindowThreadProcessId(hwnd, out processId);
                if (threadId != 0)
                {
                    GUITHREADINFO gui = new GUITHREADINFO();
                    gui.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                    if (GetGUIThreadInfo(threadId, ref gui))
                    {
                        if (gui.hwndFocus != IntPtr.Zero)
                        {
                            StringBuilder sb = new StringBuilder(256);
                            GetClassName(gui.hwndFocus, sb, sb.Capacity);
                            string focusCls = sb.ToString();
                            if (focusCls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                focusCls.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                focusCls.IndexOf("Search", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                focusCls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private bool IsFileExplorerOrDesktop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();

            if (cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("SearchBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;

            StringBuilder rootClsName = new StringBuilder(256);
            GetClassName(root, rootClsName, rootClsName.Capacity);
            string rootCls = rootClsName.ToString();

            if (cls == "CabinetWClass" || cls == "ExploreWClass" || cls == "Progman" || cls == "WorkerW" ||
                cls == "ShellTabWindowClass" || cls == "SHELLDLL_DefView" ||
                rootCls == "CabinetWClass" || rootCls == "ExploreWClass" || rootCls == "Progman" || rootCls == "WorkerW" ||
                rootCls == "ShellTabWindowClass" || rootCls == "SHELLDLL_DefView")
            {
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    }
}
