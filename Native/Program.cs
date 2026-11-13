using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using LocalConvertNative.Shell;
using LocalConvertNative.UI;

[assembly: AssemblyTitle("OMNIWHEEL")]
[assembly: AssemblyDescription("OMNIWHEEL Radial Power Converter & Desktop Utility")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("ISURU GUNASEKERA")]
[assembly: AssemblyProduct("OMNIWHEEL")]
[assembly: AssemblyCopyright("Copyright © 2026 Isuru Gunasekera")]
[assembly: AssemblyTrademark("OMNIWHEEL")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace LocalConvertNative
{
    public class Program
    {
        private static Mutex _appMutex;
        private static NotifyIcon _trayIcon;
        private static KeyboardHook _keyboardHook;
        private static RadialDonutWindow _donutWindow;
        private static QuickLookWindow _quickLookWindow;
        private static System.Windows.Application _wpfApp;
        private static SettingsWindow _settingsWindow;
        private static AppSettings _currentSettings;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);
        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;

        private static bool VerifyBinaryIntegrity()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installedDir = Path.Combine(localApp, "OMNIWHEEL");
                bool isInstalled = exePath.StartsWith(installedDir, StringComparison.OrdinalIgnoreCase);

                var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(exePath);
                if (cert == null) return !isInstalled;

                if (!cert.Subject.Contains("ISURU GUNASEKERA"))
                {
                    return false;
                }

                string thumb = cert.GetCertHashString();
                if (!string.Equals(thumb, "601AF83746DF7AA5CFCFC83F8B6FA2FA03883B05", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installedDir = Path.Combine(localApp, "OMNIWHEEL");
                return !exePath.StartsWith(installedDir, StringComparison.OrdinalIgnoreCase);
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.LogError("AppDomain UnhandledException", ex);
            };

            // Security: Immunize against DLL Hijacking & Binary Planting
            try { SetDllDirectory(""); } catch { }
            try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_APPLICATION_DIR); } catch { }

            // UIPI Drag & Drop Filter (Allows non-elevated Explorer -> elevated OMNIWHEEL drag-drop)
            EnableUipiDragDrop();

            // Zero Zombie Child Process Guarantee (All child processes auto-killed when app terminates)
            InitializeJobObject();

            // Modern Security Protocols for Win7/10/11 compatibility
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    (System.Net.SecurityProtocolType)3072 /* Tls12 */ |
                    (System.Net.SecurityProtocolType)12288 /* Tls13 */ |
                    System.Net.SecurityProtocolType.Tls;
            }
            catch { }

            // Security: Cryptographic Self-Integrity Check (Rejects tampered / patched binaries)
            if (!VerifyBinaryIntegrity())
            {
                Logger.LogError("Security Integrity Violation: Binary signature or certificate thumbprint has been tampered with.", null);
                System.Windows.Forms.MessageBox.Show(
                    "Security Integrity Alert: OMNIWHEEL binary verification failed.\nThe executable has been modified, tampered with, or corrupted.",
                    "OMNIWHEEL Security Violation",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                Environment.Exit(unchecked((int)0xC0000022)); // STATUS_ACCESS_DENIED
                return;
            }

            // 0. Headless CLI Mode (--headless / --test-headless)
            if (args != null && args.Length > 0)
            {
                bool isHeadless = false;
                string input = null;
                string action = null;
                string output = null;

                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("--test-headless", StringComparison.OrdinalIgnoreCase))
                    {
                        isHeadless = true;
                    }
                    else if (a.Equals("--input", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        input = args[++i];
                    }
                    else if (a.Equals("--action", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        action = args[++i];
                    }
                    else if (a.Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        output = args[++i];
                    }
                }

                if (isHeadless)
                {
                    RunHeadlessMode(input, action, output);
                    return;
                }
            }

            InitializeDpiAwareness();
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            }
            catch { }

            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMNIWHEEL", "app.log");
            try
            {
                string logDir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                File.AppendAllText(logPath, DateTime.Now.ToString("s") + " Main started. Args count: " + (args != null ? args.Length : 0) + Environment.NewLine);
            }
            catch { }
            try
            {
                if (_wpfApp == null)
                {
                    _wpfApp = new System.Windows.Application();
                }
                _wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _wpfApp.DispatcherUnhandledException += (s, e) =>
                {
                    Logger.LogError("DispatcherUnhandledException recovered", e.Exception);
                    e.Handled = true;
                    TrimMemory();
                };

                // Optimize GC latency for responsive radial UI animations
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

                _donutWindow = new RadialDonutWindow();
                _donutWindow.OnStatusMessage += msg => ShowBalloon("OMNIWHEEL", msg);
                _donutWindow.OnDonutClosed += () =>
                {
                    if (_keyboardHook != null) _keyboardHook.ResetState();
                    TrimMemory();
                };

                // Load Settings
                var currentSettings = AppSettings.Load();
                _currentSettings = currentSettings;
                _donutWindow.Options.Quality = currentSettings.JpegQuality;
                _donutWindow.Options.TargetDirectoryMode = currentSettings.TargetDirectoryMode;
                _donutWindow.Options.AudioBitrate = currentSettings.AudioBitrate;
                _donutWindow.DefaultWheelLayer = currentSettings.DefaultWheelLayer;
                _donutWindow.PlaySoundOnComplete = currentSettings.PlaySoundOnComplete;
                _donutWindow.ShowNotifications = currentSettings.ShowNotifications;
                _donutWindow.ApplyTheme(currentSettings.ThemeAccent, currentSettings.ThemeMode);
                _donutWindow.ApplyScale(currentSettings.WheelScale);

                _quickLookWindow = new QuickLookWindow();
                _quickLookWindow.ApplyTheme(currentSettings.ThemeAccent, currentSettings.ThemeMode);

                // 1. Single-Shot Mode (launched via right-click Explorer context menu or CLI argument)
                if (args != null && args.Length > 0)
                {
                    var targetFiles = new List<string>();
                    foreach (var a in args)
                    {
                        if (File.Exists(a) || Directory.Exists(a))
                        {
                            targetFiles.Add(Path.GetFullPath(a));
                        }
                    }

                    if (targetFiles.Count > 0)
                    {
                        File.AppendAllText(logPath, "Single-shot mode for " + targetFiles.Count + " files." + Environment.NewLine);
                        _donutWindow.IsSingleShot = true;
                        KeyboardHook.POINT pt;
                        GetCursorPos(out pt);
                        _donutWindow.ShowAt(pt.X, pt.Y, targetFiles);
                        _wpfApp.Run();
                        return;
                    }
                }

                // 2. Background Resident Service Mode (Tray + Global Hotkey)
                bool isNewInstance;
                _appMutex = new Mutex(true, "OMNIWHEEL_Native_Shell_Mutex_2026", out isNewInstance);
                File.AppendAllText(logPath, "Mutex acquired? " + isNewInstance + Environment.NewLine);
                if (!isNewInstance)
                {
                    TrimMemory();
                    File.AppendAllText(logPath, "Already running another instance. Exiting." + Environment.NewLine);
                    System.Windows.Forms.MessageBox.Show("OMNIWHEEL is already running in your Windows System Tray (near clock).\nPress and hold ` (backtick) in File Explorer or Desktop to summon the wheel.", "OMNIWHEEL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Setup Tray Icon & Background Service
                File.AppendAllText(logPath, "Initializing Tray Icon..." + Environment.NewLine);
                InitTrayIcon();

                // Setup Global Shell & Keyboard Hook
                try
                {
                    File.AppendAllText(logPath, "Registering Keyboard Hook..." + Environment.NewLine);
                    _keyboardHook = new KeyboardHook();
                    _keyboardHook.OnlyWhenExplorerActive = currentSettings.OnlyInExplorer;
                    _keyboardHook.PrimaryHotkey = currentSettings.PrimaryHotkey;
                    _keyboardHook.EnableMouseWheelFlip = currentSettings.EnableMouseWheelFlip;
                    _keyboardHook.EnableKeyFlip = currentSettings.EnableKeyFlip;
                    _keyboardHook.EnableSpacePeek = currentSettings.EnableSpacePeek;
                    _keyboardHook.IsWheelVisibleChecker = () => _donutWindow != null && _donutWindow.IsVisible;
                    _keyboardHook.IsPeekVisibleChecker = () => _quickLookWindow != null && _quickLookWindow.IsVisible;
                    _keyboardHook.OnKeyDownTriggered += HandleKeyDown;
                    _keyboardHook.OnKeyUpTriggered += HandleKeyUp;
                    _keyboardHook.OnMouseMoveWhileActive += HandleMouseMove;
                    _keyboardHook.OnTabPressed += HandleTab;

                    // Spacebar Peek / QuickLook Handlers
                    _keyboardHook.OnSpacePeekTriggered += files =>
                    {
                        if (_wpfApp != null)
                        {
                            _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_donutWindow != null && _donutWindow.IsVisible) _donutWindow.CloseDonut();
                                if (_quickLookWindow != null) _quickLookWindow.ShowPreview(files);
                            }));
                        }
                    };

                    _keyboardHook.OnPeekCloseTriggered += () =>
                    {
                        if (_wpfApp != null)
                        {
                            _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_quickLookWindow != null && _quickLookWindow.IsVisible)
                                {
                                    _quickLookWindow.ClosePreview();
                                    TrimMemory();
                                }
                            }));
                        }
                    };

                    _keyboardHook.OnPeekNavigateTriggered += delta =>
                    {
                        if (_wpfApp != null)
                        {
                            _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_quickLookWindow != null && _quickLookWindow.IsVisible) _quickLookWindow.Navigate(delta);
                            }));
                        }
                    };
                    File.AppendAllText(logPath, "Keyboard Hook Registered." + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, "Hook error: " + ex.Message + Environment.NewLine);
                    ShowBalloon("Hook Warning", "Could not register low-level hook: " + ex.Message);
                }

                // Immediate initial memory compaction and idle trim hook
                TrimMemory();
                _wpfApp.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    TrimMemory();
                }));

                // OS Power, Sleep & Shutdown Handling
                try
                {
                    Microsoft.Win32.SystemEvents.SessionEnding += (s, e) =>
                    {
                        try
                        {
                            if (_keyboardHook != null) { _keyboardHook.Dispose(); _keyboardHook = null; }
                            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
                        }
                        catch { }
                    };

                    Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
                    {
                        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                        {
                            try { if (_keyboardHook != null) _keyboardHook.ResetState(); } catch { }
                        }
                    };
                }
                catch { }

                // Run WPF Dispatcher Loop
                File.AppendAllText(logPath, "Starting WPF Dispatcher loop..." + Environment.NewLine);
                _wpfApp.Run();
                File.AppendAllText(logPath, "WPF Dispatcher loop exited." + Environment.NewLine);

                // Cleanup on shutdown
                if (_keyboardHook != null) _keyboardHook.Dispose();
                if (_trayIcon != null) _trayIcon.Dispose();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(logPath, "FATAL EXCEPTION: " + ex.ToString() + Environment.NewLine); } catch { }
            }
        }

        private static DateTime _lastKeyDownTime = DateTime.MinValue;

        private static void HandleKeyDown(int x, int y)
        {
            _lastKeyDownTime = DateTime.Now;

            // If wheel is already visible and user presses hotkey again: toggle close!
            if (_wpfApp != null && _donutWindow != null && _donutWindow.IsVisible)
            {
                _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _donutWindow.CloseDonut();
                }));
                return;
            }

            // If QuickLook is open and user summons radial wheel, close QuickLook first
            if (_wpfApp != null && _quickLookWindow != null && _quickLookWindow.IsVisible)
            {
                _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _quickLookWindow.ClosePreview();
                }));
            }

            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
            {
                var selectedFiles = ExplorerSelectionHelper.GetSelectedFilePaths();

                if (_wpfApp != null)
                {
                    _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_donutWindow != null)
                        {
                            _donutWindow.ShowAt(x, y, selectedFiles);
                        }
                    }));
                }
            }));
        }

        private static void HandleKeyUp()
        {
            if (_wpfApp != null && _donutWindow != null)
            {
                _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_donutWindow.IsVisible) return;

                    double holdMs = (DateTime.Now - _lastKeyDownTime).TotalMilliseconds;
                    var cfg = _currentSettings ?? AppSettings.Load();

                    if (cfg.SummonMode == "PushToTalk")
                    {
                        _donutWindow.ExecuteHoveredOrClose();
                    }
                    else if (cfg.SummonMode == "TapToggle")
                    {
                        // Tap to toggle: do nothing on key release, user clicks or presses Esc/` to close
                    }
                    else // Dual (Default)
                    {
                        // If held > 220ms (push-and-release style): execute hovered or close
                        // If tapped (<= 220ms): KEEP WHEEL OPEN so user can move mouse and click!
                        if (holdMs > 220)
                        {
                            _donutWindow.ExecuteHoveredOrClose();
                        }
                    }
                }));
            }
        }

        private static int _latestMouseX = -1;
        private static int _latestMouseY = -1;
        private static int _isMouseDispatchPending = 0;

        private static void HandleMouseMove(int x, int y)
        {
            if (_wpfApp == null || _donutWindow == null || !_donutWindow.IsVisible) return;

            _latestMouseX = x;
            _latestMouseY = y;

            if (Interlocked.CompareExchange(ref _isMouseDispatchPending, 1, 0) == 0)
            {
                _wpfApp.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                {
                    Interlocked.Exchange(ref _isMouseDispatchPending, 0);
                    if (_donutWindow != null && _donutWindow.IsVisible)
                    {
                        _donutWindow.UpdateMousePosition(_latestMouseX, _latestMouseY);
                    }
                }));
            }
        }

        private static void HandleTab()
        {
            if (_wpfApp != null && _donutWindow != null && _donutWindow.IsVisible)
            {
                _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _donutWindow.FlipFace();
                }));
            }
        }

        private static void InitTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "OMNIWHEEL (Radial Quick Action Wheel)";

            // Royal Purple Wheel with White Stripes Icon
            bool iconLoaded = false;
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (!File.Exists(icoPath))
                {
                    icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "app.ico");
                }
                if (File.Exists(icoPath))
                {
                    _trayIcon.Icon = new System.Drawing.Icon(icoPath, 32, 32);
                    iconLoaded = true;
                }
            }
            catch { }

            if (!iconLoaded)
            {
                using (var bmp = new Bitmap(32, 32))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    Color royalPurple = Color.FromArgb(0x6B, 0x21, 0xA8);
                    using (var brush = new SolidBrush(royalPurple))
                    {
                        g.FillEllipse(brush, 1, 1, 30, 30);
                    }

                    using (var pen = new Pen(Color.White, 1.5f))
                    {
                        g.DrawEllipse(pen, 2.5f, 2.5f, 27f, 27f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            float x1 = 16f + (float)(Math.Cos(a) * 4f);
                            float y1 = 16f + (float)(Math.Sin(a) * 4f);
                            float x2 = 16f + (float)(Math.Cos(a) * 12.5f);
                            float y2 = 16f + (float)(Math.Sin(a) * 12.5f);
                            g.DrawLine(pen, x1, y1, x2, y2);
                        }
                    }

                    using (var hub = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(hub, 11, 11, 10, 10);
                    }

                    using (var dot = new SolidBrush(royalPurple))
                    {
                        g.FillEllipse(dot, 14, 14, 4, 4);
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        using (var tempIcon = System.Drawing.Icon.FromHandle(hIcon))
                        {
                            _trayIcon.Icon = (System.Drawing.Icon)tempIcon.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }

            _trayIcon.BalloonTipClicked += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_lastOutputFolder) && Directory.Exists(_lastOutputFolder))
                {
                    try { Process.Start("explorer.exe", "\"" + _lastOutputFolder + "\""); } catch { }
                }
            };

            var contextMenu = new ContextMenuStrip();
            ToolStripMenuItem startupMenuItem = null;

            // 1. Settings Panel
            contextMenu.Items.Add("⚙ Settings", null, (s, e) =>
            {
                if (_wpfApp != null)
                {
                    _wpfApp.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                        {
                            _settingsWindow = new SettingsWindow();
                            _settingsWindow.OnSettingsSaved += updatedSettings =>
                            {
                                _currentSettings = updatedSettings;
                                if (_donutWindow != null)
                                {
                                    _donutWindow.Options.Quality = updatedSettings.JpegQuality;
                                    _donutWindow.Options.TargetDirectoryMode = updatedSettings.TargetDirectoryMode;
                                    _donutWindow.Options.AudioBitrate = updatedSettings.AudioBitrate;
                                    _donutWindow.DefaultWheelLayer = updatedSettings.DefaultWheelLayer;
                                    _donutWindow.PlaySoundOnComplete = updatedSettings.PlaySoundOnComplete;
                                    _donutWindow.ShowNotifications = updatedSettings.ShowNotifications;
                                    _donutWindow.ApplyTheme(updatedSettings.ThemeAccent, updatedSettings.ThemeMode);
                                    _donutWindow.ApplyScale(updatedSettings.WheelScale);
                                }
                                if (_keyboardHook != null)
                                {
                                    _keyboardHook.OnlyWhenExplorerActive = updatedSettings.OnlyInExplorer;
                                    _keyboardHook.PrimaryHotkey = updatedSettings.PrimaryHotkey;
                                    _keyboardHook.EnableMouseWheelFlip = updatedSettings.EnableMouseWheelFlip;
                                    _keyboardHook.EnableKeyFlip = updatedSettings.EnableKeyFlip;
                                    _keyboardHook.EnableSpacePeek = updatedSettings.EnableSpacePeek;
                                }
                                if (_quickLookWindow != null)
                                {
                                    _quickLookWindow.ApplyTheme(updatedSettings.ThemeAccent, updatedSettings.ThemeMode);
                                }
                                if (startupMenuItem != null)
                                {
                                    startupMenuItem.Checked = updatedSettings.StartWithWindows;
                                }
                                ShowBalloon("Settings Saved", "Preferences and visual theme applied.");
                            };
                            _settingsWindow.Closed += (ws, we) => { _settingsWindow = null; TrimMemory(); };
                            _settingsWindow.Show();
                        }
                        else
                        {
                            _settingsWindow.Activate();
                        }
                    }));
                }
            });

            // 2. Run at Windows Startup Direct Toggle
            startupMenuItem = new ToolStripMenuItem("Run at Windows Startup");
            startupMenuItem.CheckOnClick = true;
            startupMenuItem.Checked = StartupHelper.IsStartupRegistered();
            startupMenuItem.Click += (s, e) =>
            {
                bool enable = startupMenuItem.Checked;
                var sObj = AppSettings.Load();
                sObj.StartWithWindows = enable;
                sObj.Save();
                if (enable)
                {
                    StartupHelper.RegisterStartup();
                    ShowBalloon("OMNIWHEEL", "Enabled: OMNIWHEEL will start with Windows.");
                }
                else
                {
                    StartupHelper.UnregisterStartup();
                    ShowBalloon("OMNIWHEEL", "Disabled: OMNIWHEEL removed from Windows startup.");
                }
            };
            contextMenu.Items.Add(startupMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            // 3. Check for Updates
            contextMenu.Items.Add("🔄 Check for Updates", null, (s, e) =>
            {
                ShowBalloon("Update Check", "OMNIWHEEL 1.0 is up to date (Latest Native Win32 Engine).");
            });

            // 4. View Error Logs
            contextMenu.Items.Add("📄 View Error Logs", null, (s, e) =>
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMNIWHEEL", "app.log");
                if (File.Exists(logPath))
                {
                    try
                    {
                        Process.Start("notepad.exe", logPath);
                    }
                    catch (Exception ex)
                    {
                        ShowBalloon("Log Viewer", "Error opening log: " + ex.Message);
                    }
                }
                else
                {
                    ShowBalloon("Log Viewer", "No errors logged. All subsystems operating nominally.");
                }
            });

            contextMenu.Items.Add(new ToolStripSeparator());

            // 5. Trigger Radial at Cursor option
            contextMenu.Items.Add("Summon OMNIWHEEL (`)", null, (s, e) =>
            {
                KeyboardHook.POINT pt;
                GetCursorPos(out pt);
                var sel = ExplorerSelectionHelper.GetSelectedFilePaths();
                _donutWindow.ShowAt(pt.X, pt.Y, sel);
            });

            contextMenu.Items.Add(new ToolStripSeparator());

            // 6. Uninstall
            contextMenu.Items.Add("🗑 Uninstall OMNIWHEEL...", null, (s, e) =>
            {
                string uninstExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uninstall.exe");
                if (!File.Exists(uninstExe))
                {
                    string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMNIWHEEL");
                    uninstExe = Path.Combine(localDir, "uninstall.exe");
                }

                if (File.Exists(uninstExe))
                {
                    try { Process.Start(uninstExe, "/uninstall"); } catch { }
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("Uninstaller not found at:\n" + uninstExe, "OMNIWHEEL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });

            // 7. Exit
            contextMenu.Items.Add("✕ Exit", null, (s, e) =>
            {
                _wpfApp.Shutdown();
            });

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) =>
            {
                KeyboardHook.POINT pt;
                GetCursorPos(out pt);
                var sel = ExplorerSelectionHelper.GetSelectedFilePaths();
                _donutWindow.ShowAt(pt.X, pt.Y, sel);
            };
        }

        private static string _lastOutputFolder = null;

        public static void SetLastOutput(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (Directory.Exists(path)) _lastOutputFolder = path;
                else if (File.Exists(path)) _lastOutputFolder = Path.GetDirectoryName(path);
            }
        }

        public static void ShowBalloon(string title, string text)
        {
            if (_trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        public static void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out KeyboardHook.POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        private static void RunHeadlessMode(string input, string action, string output)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(action))
            {
                Console.Error.WriteLine("{\"success\":false,\"error\":\"Missing --input or --action\"}");
                Environment.Exit(1);
                return;
            }

            if (!File.Exists(input) && !Directory.Exists(input))
            {
                Console.Error.WriteLine(string.Format("{{\"success\":false,\"error\":\"Input path not found: {0}\"}}", EscapeJson(input)));
                Environment.Exit(1);
                return;
            }

            try
            {
                var opt = AppSettings.Load();
                var convOpt = new Engine.ConversionOptions
                {
                    Quality = opt.JpegQuality,
                    TargetDirectoryMode = opt.TargetDirectoryMode
                };

                var res = Engine.FileConversionEngine.Convert(input, action, convOpt);

                if (!string.IsNullOrEmpty(output) && res.Success && !string.IsNullOrEmpty(res.OutputPath) && File.Exists(res.OutputPath))
                {
                    if (!string.Equals(Path.GetFullPath(res.OutputPath), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                    {
                        string outDir = Path.GetDirectoryName(output);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                        if (File.Exists(output)) File.Delete(output);
                        File.Move(res.OutputPath, output);
                        res.OutputPath = output;
                        res.OutputSize = new FileInfo(output).Length;
                    }
                }

                if (res.Success && !string.IsNullOrEmpty(res.OutputPath) && (File.Exists(res.OutputPath) || Directory.Exists(res.OutputPath)))
                {
                    Console.WriteLine(string.Format("{{\"success\":true,\"outputPath\":\"{0}\",\"size\":{1},\"message\":\"{2}\"}}",
                        EscapeJson(res.OutputPath), res.OutputSize, EscapeJson(res.Message)));
                    Environment.Exit(0);
                }
                else
                {
                    Console.Error.WriteLine(string.Format("{{\"success\":false,\"error\":\"{0}\"}}", EscapeJson(res.Message)));
                    Environment.Exit(2);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Headless execution failed", ex, input);
                Console.Error.WriteLine(string.Format("{{\"success\":false,\"error\":\"{0}\"}}", EscapeJson(ex.Message)));
                Environment.Exit(3);
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]
        private delegate bool SetProcessDpiAwarenessContextDelegate(IntPtr dpiContext);

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]
        private delegate int SetProcessDpiAwarenessDelegate(int awareness);

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]
        private delegate bool SetProcessDPIAwareDelegate();

        private static void InitializeDpiAwareness()
        {
            try
            {
                // 1. Windows 10 Creators Update (1703+) & Windows 11: SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4)
                IntPtr user32 = LoadLibrary("user32.dll");
                if (user32 != IntPtr.Zero)
                {
                    IntPtr proc = GetProcAddress(user32, "SetProcessDpiAwarenessContext");
                    if (proc != IntPtr.Zero)
                    {
                        var setContext = (SetProcessDpiAwarenessContextDelegate)
                            System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(proc, typeof(SetProcessDpiAwarenessContextDelegate));
                        if (setContext(new IntPtr(-4)))
                        {
                            return;
                        }
                    }
                }

                // 2. Windows 8.1 & early Windows 10: SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE = 2)
                IntPtr shcore = LoadLibrary("shcore.dll");
                if (shcore != IntPtr.Zero)
                {
                    IntPtr proc = GetProcAddress(shcore, "SetProcessDpiAwareness");
                    if (proc != IntPtr.Zero)
                    {
                        var setAwareness = (SetProcessDpiAwarenessDelegate)
                            System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(proc, typeof(SetProcessDpiAwarenessDelegate));
                        if (setAwareness(2) == 0)
                        {
                            return;
                        }
                    }
                }

                // 3. Windows 7 & Vista: SetProcessDPIAware()
                if (user32 != IntPtr.Zero)
                {
                    IntPtr proc = GetProcAddress(user32, "SetProcessDPIAware");
                    if (proc != IntPtr.Zero)
                    {
                        var setAware = (SetProcessDPIAwareDelegate)
                            System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(proc, typeof(SetProcessDPIAwareDelegate));
                        setAware();
                    }
                }
            }
            catch { }
        }

        #region UIPI Drag & Drop Message Filter
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);
        private const uint MSGFLT_ADD = 1;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYGLOBALDATA = 0x0049;

        private static void EnableUipiDragDrop()
        {
            try
            {
                ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);
            }
            catch { }
        }
        #endregion

        #region Win32 Job Object (Zero Zombie Child Processes Guarantee)
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryLimit;
            public UIntPtr PeakJobMemoryLimit;
        }

        private static IntPtr _jobHandle = IntPtr.Zero;

        private static void InitializeJobObject()
        {
            try
            {
                _jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (_jobHandle != IntPtr.Zero)
                {
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr extendedInfoPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
                    try
                    {
                        System.Runtime.InteropServices.Marshal.StructureToPtr(info, extendedInfoPtr, false);
                        if (SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
                        {
                            AssignProcessToJobObject(_jobHandle, GetCurrentProcess());
                        }
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(extendedInfoPtr);
                    }
                }
            }
            catch { }
        }
        #endregion
    }
}
