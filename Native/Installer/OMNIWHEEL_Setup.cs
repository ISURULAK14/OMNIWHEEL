using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("OMNIWHEEL Setup Wizard")]
[assembly: AssemblyDescription("OMNIWHEEL Standalone Installer & Setup Wizard")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("ISURU GUNASEKERA")]
[assembly: AssemblyProduct("OMNIWHEEL")]
[assembly: AssemblyCopyright("Copyright © 2026 Isuru Gunasekera")]
[assembly: AssemblyTrademark("OMNIWHEEL")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OMNIWHEEL.Installer
{
    public class SetupApp : Application
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);
        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;

        [STAThread]
        public static void Main(string[] args)
        {
            // Security: Immunize installer against DLL Hijacking & Binary Planting
            try { SetDllDirectory(""); } catch { }
            try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_APPLICATION_DIR); } catch { }

            string currentExe = "";
            try { currentExe = Assembly.GetExecutingAssembly().Location; } catch { }
            bool isUninstallExe = !string.IsNullOrEmpty(currentExe) &&
                Path.GetFileName(currentExe).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase);

            bool isSilent = false;
            bool isUninstall = isUninstallExe;

            if (args != null && args.Length > 0)
            {
                foreach (string a in args)
                {
                    if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                    {
                        isSilent = true;
                    }
                    if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        isUninstall = true;
                    }
                }
            }

            if (isUninstall)
            {
                if (isSilent)
                {
                    PerformUninstall(GetDefaultInstallDir(), true);
                    Environment.Exit(0);
                    return;
                }

                var uninstApp = new SetupApp();
                uninstApp.Run(new UninstallWindow());
                return;
            }

            if (isSilent)
            {
                bool ok = PerformInstall(GetDefaultInstallDir(), true, true, true, true, false, true);
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            var app = new SetupApp();
            app.Run(new SetupWindow());
        }

        public static string GetDefaultInstallDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "OMNIWHEEL");
        }

        public static bool PerformInstall(string targetDir, bool createDesktop, bool createStartMenu, bool registerContext, bool runOnStartup, bool launchNow, bool isSilent = false)
        {
            try
            {
                if (string.IsNullOrEmpty(targetDir)) targetDir = GetDefaultInstallDir();
                targetDir = targetDir.Trim().TrimEnd('\\', '/');

                // 0. Gracefully close or terminate any currently running OMNIWHEEL instance to avoid file locks
                try
                {
                    foreach (var p in Process.GetProcessesByName("OMNIWHEEL"))
                    {
                        try
                        {
                            p.CloseMainWindow();
                            if (!p.WaitForExit(1500))
                            {
                                p.Kill();
                                p.WaitForExit(1000);
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Security: Restrict NTFS DACL on target installation directory to Current User, System, and Local Admins
                try
                {
                    var dInfo = new DirectoryInfo(targetDir);
                    var dSecurity = dInfo.GetAccessControl();
                    var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                    var adminSid = new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                    var systemSid = new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.LocalSystemSid, null);

                    dSecurity.SetAccessRuleProtection(true, false);
                    dSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        user,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                        System.Security.AccessControl.PropagationFlags.None,
                        System.Security.AccessControl.AccessControlType.Allow));
                    dSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        adminSid,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                        System.Security.AccessControl.PropagationFlags.None,
                        System.Security.AccessControl.AccessControlType.Allow));
                    dSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        systemSid,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                        System.Security.AccessControl.PropagationFlags.None,
                        System.Security.AccessControl.AccessControlType.Allow));

                    dInfo.SetAccessControl(dSecurity);
                }
                catch { }

                // 1. Extract embedded payload files
                ExtractResource("OMNIWHEEL.exe", Path.Combine(targetDir, "OMNIWHEEL.exe"));
                ExtractResource("OMNIWHEEL.exe.config", Path.Combine(targetDir, "OMNIWHEEL.exe.config"));
                ExtractResource("app.ico", Path.Combine(targetDir, "app.ico"));
                ExtractResource("WindowsOcrEngine.ps1", Path.Combine(targetDir, "WindowsOcrEngine.ps1"));
                ExtractResource("D3DCompiler_47_cor3.dll", Path.Combine(targetDir, "D3DCompiler_47_cor3.dll"));
                ExtractResource("PenImc_cor3.dll", Path.Combine(targetDir, "PenImc_cor3.dll"));
                ExtractResource("PresentationNative_cor3.dll", Path.Combine(targetDir, "PresentationNative_cor3.dll"));
                ExtractResource("vcruntime140_cor3.dll", Path.Combine(targetDir, "vcruntime140_cor3.dll"));
                ExtractResource("wpfgfx_cor3.dll", Path.Combine(targetDir, "wpfgfx_cor3.dll"));

                // 2. Extract pre-configured settings.ini
                try
                {
                    string appDataSettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OMNIWHEEL");
                    if (!Directory.Exists(appDataSettingsDir)) Directory.CreateDirectory(appDataSettingsDir);
                    ExtractResource("settings.ini", Path.Combine(appDataSettingsDir, "settings.ini"));
                    ExtractResource("settings.ini", Path.Combine(targetDir, "settings.ini"));
                }
                catch { }

                // 3. Extract ffmpeg engine (embedded zip or local copy)
                ExtractEmbeddedFfmpeg(targetDir);

                // 4. Copy self as uninstaller
                string currentExe = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    string targetUninstaller = Path.Combine(targetDir, "uninstall.exe");
                    try { File.Copy(currentExe, targetUninstaller, true); } catch { }
                }

                string exePath = Path.Combine(targetDir, "OMNIWHEEL.exe");

                // 5. Create Shortcuts (Desktop & Start Menu)
                if (createDesktop)
                {
                    string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktopDir, "OMNIWHEEL.lnk"), exePath, targetDir, "OMNIWHEEL - Radial Power Converter");
                }

                if (createStartMenu)
                {
                    string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    string omniMenuDir = Path.Combine(programsDir, "OMNIWHEEL");
                    if (!Directory.Exists(omniMenuDir)) Directory.CreateDirectory(omniMenuDir);
                    CreateShortcut(Path.Combine(omniMenuDir, "OMNIWHEEL.lnk"), exePath, targetDir, "OMNIWHEEL - Radial Power Converter");

                    string uninstExe = Path.Combine(targetDir, "uninstall.exe");
                    if (File.Exists(uninstExe))
                    {
                        CreateShortcut(Path.Combine(omniMenuDir, "Uninstall OMNIWHEEL.lnk"), uninstExe, targetDir, "Uninstall OMNIWHEEL from this computer");
                    }
                }

                // 6. Explorer Right-Click Context Menu
                if (registerContext)
                {
                    RegisterContextMenu(exePath);
                }

                // 7. Windows Startup
                if (runOnStartup)
                {
                    using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (runKey != null) runKey.SetValue("OMNIWHEEL", "\"" + exePath + "\"");
                    }
                }

                // 8. Register Windows Add/Remove Programs entry
                RegisterUninstallEntry(targetDir, exePath);

                // 9. Launch OMNIWHEEL if requested
                if (launchNow && File.Exists(exePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetDir });
                    }
                    catch { }
                }

                return true;
            }
            catch (UnauthorizedAccessException uex)
            {
                if (!isSilent)
                {
                    MessageBox.Show("Installation failed due to permission limits:\n" + uex.Message +
                        "\n\nIf installing to Program Files or a protected folder, please run Setup as Administrator.",
                        "OMNIWHEEL Setup Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
            catch (Exception ex)
            {
                if (!isSilent)
                {
                    MessageBox.Show("Installation failed:\n" + ex.Message, "OMNIWHEEL Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        public static void PerformUninstall(string targetDir, bool isSilent = false)
        {
            try
            {
                // 1. Terminate running instances
                try
                {
                    foreach (var p in Process.GetProcessesByName("OMNIWHEEL"))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }

                // 2. Remove shortcuts
                try
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OMNIWHEEL.lnk");
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                    string programsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OMNIWHEEL");
                    if (Directory.Exists(programsDir)) Directory.Delete(programsDir, true);
                }
                catch { }

                // 3. Remove context menu
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\OMNIWHEEL", false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\OMNIWHEEL", false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\OMNIWHEEL", false);
                }
                catch { }

                // 4. Remove startup entry
                try
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (runKey != null) runKey.DeleteValue("OMNIWHEEL", false);
                    }
                }
                catch { }

                // 5. Remove uninstall entry
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OMNIWHEEL", false);
                }
                catch { }

                // 6. Remove installed files and directory
                try
                {
                    if (Directory.Exists(targetDir))
                    {
                        foreach (string file in Directory.GetFiles(targetDir))
                        {
                            try
                            {
                                if (!Path.GetFileName(file).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Delete(file);
                                }
                            }
                            catch { }
                        }

                        string selfExe = Assembly.GetExecutingAssembly().Location;
                        bool isRunningFromTarget = !string.IsNullOrEmpty(selfExe) &&
                            selfExe.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase);

                        if (isRunningFromTarget)
                        {
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                                Arguments = string.Format("/C choice /C Y /N /D Y /T 2 & rmdir /s /q \"{0}\"", targetDir),
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);
                        }
                        else
                        {
                            Directory.Delete(targetDir, true);
                        }
                    }

                    // 7. Remove Roaming AppData settings folder to guarantee zero orphaned files
                    string appDataSettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OMNIWHEEL");
                    if (Directory.Exists(appDataSettingsDir)) Directory.Delete(appDataSettingsDir, true);
                }
                catch { }

                if (!isSilent)
                {
                    MessageBox.Show("OMNIWHEEL has been completely uninstalled from this computer.", "OMNIWHEEL Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (!isSilent)
                {
                    MessageBox.Show("Uninstall note: " + ex.Message, "OMNIWHEEL Uninstall", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void RegisterContextMenu(string exePath)
        {
            string[] keys = new string[]
            {
                @"Software\Classes\*\shell\OMNIWHEEL",
                @"Software\Classes\Directory\shell\OMNIWHEEL",
                @"Software\Classes\Directory\Background\shell\OMNIWHEEL"
            };

            foreach (string path in keys)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
                {
                    if (key != null)
                    {
                        key.SetValue("", "OMNIWHEEL (Donut)");
                        key.SetValue("Icon", exePath + ",0");

                        using (RegistryKey cmdKey = key.CreateSubKey("command"))
                        {
                            if (cmdKey != null)
                            {
                                if (path.Contains("Background"))
                                    cmdKey.SetValue("", string.Format("\"{0}\" \"%V\"", exePath));
                                else
                                    cmdKey.SetValue("", string.Format("\"{0}\" \"%1\"", exePath));
                            }
                        }
                    }
                }
            }
        }

        private static void RegisterUninstallEntry(string targetDir, string exePath)
        {
            try
            {
                string uninstPath = Path.Combine(targetDir, "uninstall.exe");
                string uninstCmd = File.Exists(uninstPath) ?
                    string.Format("\"{0}\" /uninstall", uninstPath) :
                    string.Format("\"{0}\" /uninstall", exePath);

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OMNIWHEEL"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "OMNIWHEEL Radial Converter & Utility");
                        key.SetValue("DisplayVersion", "1.0.0");
                        key.SetValue("Publisher", "OMNIWHEEL Native Tools");
                        key.SetValue("DisplayIcon", exePath + ",0");
                        key.SetValue("InstallLocation", targetDir);
                        key.SetValue("UninstallString", uninstCmd);
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    shell = Activator.CreateInstance(shellType);
                    dynamic dShell = shell;
                    shortcut = dShell.CreateShortcut(shortcutPath);
                    dynamic dShortcut = shortcut;
                    dShortcut.TargetPath = targetPath;
                    dShortcut.WorkingDirectory = workingDir;
                    dShortcut.Description = description;
                    dShortcut.IconLocation = targetPath + ",0";
                    dShortcut.Save();
                }
            }
            catch { }
            finally
            {
                if (shortcut != null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void ExtractResource(string resourceName, string outputPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            // Look for matching resource name
            string foundName = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                {
                    foundName = name;
                    break;
                }
            }

            if (foundName == null)
            {
                // Fallback to local file if run alongside source tree
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resourceName);
                if (File.Exists(localFile))
                {
                    File.Copy(localFile, outputPath, true);
                    return;
                }
                throw new FileNotFoundException("Embedded resource not found: " + resourceName);
            }

            using (Stream input = asm.GetManifestResourceStream(foundName))
            using (FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[65536];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                }
            }
        }

        private static void ExtractEmbeddedFfmpeg(string targetDir)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string foundName = null;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("ffmpeg.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        foundName = name;
                        break;
                    }
                }

                if (foundName != null)
                {
                    using (Stream zipStream = asm.GetManifestResourceStream(foundName))
                    using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                string dest = Path.Combine(targetDir, "ffmpeg.exe");
                                entry.ExtractToFile(dest, true);
                            }
                        }
                    }
                    return;
                }
            }
            catch { }
        }

        public static void Launch3DStudio()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string htmlPath = Path.Combine(baseDir, "OMNIWHEEL_3D_Frosted_UI.html");
                if (!File.Exists(htmlPath))
                {
                    string localApp = GetDefaultInstallDir();
                    htmlPath = Path.Combine(localApp, "OMNIWHEEL_3D_Frosted_UI.html");
                }
                if (!File.Exists(htmlPath))
                {
                    htmlPath = @"E:\ANTIGRAVITY\OMNIWHEEL\OMNIWHEEL_3D_Frosted_UI.html";
                }
                if (File.Exists(htmlPath))
                {
                    Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
                }
            }
            catch { }
        }
    }

    // ======================================================================
    // THREE.JS REALISTIC PBR TEXTURE & 3D HARDWARE-ACCELERATED BACKDROP
    // ======================================================================
    public class RealisticThreeJsBackground : Grid
    {
        private Viewport3D _viewport;
        private PerspectiveCamera _camera;
        private AxisAngleRotation3D _rotOuterY;
        private AxisAngleRotation3D _rotOuterX;
        private AxisAngleRotation3D _rotInnerY;
        private AxisAngleRotation3D _rotInnerX;
        private AxisAngleRotation3D _rotInnerZ;
        private AxisAngleRotation3D _rotTrackZ;
        private AxisAngleRotation3D _rotTrackX;
        private TranslateTransform3D _masterFloat;
        private TranslateTransform3D[] _nodeTrans;
        private DispatcherTimer _animTimer;
        private Stopwatch _stopwatch;

        private Border _glowOrb1;
        private Border _glowOrb2;
        private Border _glowOrb3;
        private TranslateTransform _glow1Trans;
        private TranslateTransform _glow2Trans;
        private TranslateTransform _glow3Trans;

        private double _targetCamX = 0;
        private double _targetCamY = 0;
        private double _camX = 0;
        private double _camY = 0;

        public RealisticThreeJsBackground(bool isUninstallMode)
        {
            ClipToBounds = true;
            IsHitTestVisible = false;

            // 1. Deep Curd White / Pearl Base with Multi-Stop PBR Radial Illumination
            var baseBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.45),
                GradientOrigin = new Point(0.5, 0.45),
                RadiusX = 0.88,
                RadiusY = 0.88
            };
            if (!isUninstallMode)
            {
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFD, 0xFB, 0xF7), 0.0));
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF5, 0xEF, 0xE5), 0.42));
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xEA, 0xE2, 0xD4), 0.78));
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xDE, 0xD5, 0xC5), 1.0));
            }
            else
            {
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFC, 0xFA, 0xF6), 0.0));
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4, 0xEE, 0xE6), 0.42));
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0xDF, 0xD5), 0.78));
                baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xDB, 0xD0, 0xC4), 1.0));
            }
            Background = baseBrush;

            // 2. Realistic Specular Caustic Glow Orbs (Simulating Three.js dynamic point lights & transmission)
            _glowOrb1 = new Border
            {
                Width = 320,
                Height = 320,
                CornerRadius = new CornerRadius(160),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-60, 40, 0, 0),
                Opacity = isUninstallMode ? 0.22 : 0.32,
                Background = new RadialGradientBrush(
                    Color.FromArgb(0xDD, 0xFF, 0x85, 0x1A), // Vibrant Electric Sunset
                    Color.FromArgb(0x00, 0xFF, 0x85, 0x1A))
            };
            _glow1Trans = new TranslateTransform();
            _glowOrb1.RenderTransform = _glow1Trans;
            _glowOrb1.Effect = new DropShadowEffect
            {
                BlurRadius = 90,
                ShadowDepth = 0,
                Opacity = 0.70,
                Color = Color.FromRgb(0xFF, 0x7A, 0x00)
            };
            Children.Add(_glowOrb1);

            _glowOrb2 = new Border
            {
                Width = 300,
                Height = 300,
                CornerRadius = new CornerRadius(150),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -60, 60),
                Opacity = isUninstallMode ? 0.20 : 0.28,
                Background = new RadialGradientBrush(
                    Color.FromArgb(0xDD, 0x8B, 0x5C, 0xF6), // Royal Electric Purple
                    Color.FromArgb(0x00, 0x8B, 0x5C, 0xF6))
            };
            _glow2Trans = new TranslateTransform();
            _glowOrb2.RenderTransform = _glow2Trans;
            _glowOrb2.Effect = new DropShadowEffect
            {
                BlurRadius = 85,
                ShadowDepth = 0,
                Opacity = 0.65,
                Color = Color.FromRgb(0x8B, 0x5C, 0xF6)
            };
            Children.Add(_glowOrb2);

            _glowOrb3 = new Border
            {
                Width = 240,
                Height = 240,
                CornerRadius = new CornerRadius(120),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = isUninstallMode ? 0.14 : 0.22,
                Background = new RadialGradientBrush(
                    Color.FromArgb(0xBB, 0x38, 0xBD, 0xF8), // Sky Blue Caustic Highlight
                    Color.FromArgb(0x00, 0x38, 0xBD, 0xF8))
            };
            _glow3Trans = new TranslateTransform();
            _glowOrb3.RenderTransform = _glow3Trans;
            _glowOrb3.Effect = new DropShadowEffect
            {
                BlurRadius = 75,
                ShadowDepth = 0,
                Opacity = 0.55,
                Color = Color.FromRgb(0x38, 0xBD, 0xF8)
            };
            Children.Add(_glowOrb3);

            // 3. Hardware-Accelerated Viewport3D with PBR Torus / Radial Gyroscope System
            _viewport = new Viewport3D
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = isUninstallMode ? 0.42 : 0.52
            };

            _camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0, 8.2),
                LookDirection = new Vector3D(0, 0, -1),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 42
            };
            _viewport.Camera = _camera;

            var modelGroup = new Model3DGroup();

            // Lighting (Three.js Studio Key, Fill, Rim & Caustic Bounce)
            var keyLight = new DirectionalLight(Color.FromRgb(0xFF, 0xA8, 0x34), new Vector3D(-1.4, 1.8, -2.2));
            var fillLight = new DirectionalLight(Color.FromRgb(0x8B, 0x5C, 0xF6), new Vector3D(1.6, -1.2, -1.8));
            var rimLight = new DirectionalLight(Color.FromRgb(0xFF, 0xFF, 0xFF), new Vector3D(0, -0.6, -3.4));
            var bounceLight = new DirectionalLight(Color.FromRgb(0x38, 0xBD, 0xF8), new Vector3D(0, 2.0, 0.6));
            var ambientLight = new AmbientLight(Color.FromRgb(0x56, 0x4E, 0x60));

            modelGroup.Children.Add(keyLight);
            modelGroup.Children.Add(fillLight);
            modelGroup.Children.Add(rimLight);
            modelGroup.Children.Add(bounceLight);
            modelGroup.Children.Add(ambientLight);

            // Master Kinetic Container (Three.js Organic Floating Breath)
            var masterGroup = new Model3DGroup();
            _masterFloat = new TranslateTransform3D(0, 0, 0);
            masterGroup.Transform = _masterFloat;

            // 3A. Primary Outer Rotor (OmniWheel Primary Torus)
            var outerMesh = GenerateTorusMesh(2.38, 0.40, 36, 18);
            var outerMat = new MaterialGroup();
            outerMat.Children.Add(new DiffuseMaterial(new LinearGradientBrush(
                Color.FromArgb(0xF6, 0xFF, 0xFD, 0xF8),
                Color.FromArgb(0xD8, 0xEA, 0xE2, 0xD4),
                new Point(0, 0), new Point(1, 1))));
            outerMat.Children.Add(new SpecularMaterial(Brushes.White, 85.0));
            var outerModel = new GeometryModel3D(outerMesh, outerMat) { BackMaterial = outerMat };

            var outerTg = new Transform3DGroup();
            _rotOuterY = new AxisAngleRotation3D(new Vector3D(0.18, 1.0, 0.12), 0);
            _rotOuterX = new AxisAngleRotation3D(new Vector3D(1.0, 0.12, 0.18), 16);
            outerTg.Children.Add(new RotateTransform3D(_rotOuterY));
            outerTg.Children.Add(new RotateTransform3D(_rotOuterX));
            outerModel.Transform = outerTg;
            masterGroup.Children.Add(outerModel);

            // 3B. Counter-Rotating Inner Gyroscopic Ring (Royal Amethyst Torus)
            var innerMesh = GenerateTorusMesh(1.58, 0.26, 30, 16);
            var innerMat = new MaterialGroup();
            innerMat.Children.Add(new DiffuseMaterial(new LinearGradientBrush(
                Color.FromArgb(0xF0, 0xF5, 0xEE, 0xFD),
                Color.FromArgb(0xD2, 0xC0, 0x84, 0xFC),
                new Point(0, 0), new Point(1, 1))));
            innerMat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xDD, 0xD6, 0xFE)), 95.0));
            var innerModel = new GeometryModel3D(innerMesh, innerMat) { BackMaterial = innerMat };

            var innerTg = new Transform3DGroup();
            _rotInnerY = new AxisAngleRotation3D(new Vector3D(-0.25, 1.0, -0.3), 0);
            _rotInnerX = new AxisAngleRotation3D(new Vector3D(1.0, 0.2, -0.2), -20);
            _rotInnerZ = new AxisAngleRotation3D(new Vector3D(0.2, 0.2, 1.0), 0);
            innerTg.Children.Add(new RotateTransform3D(_rotInnerY));
            innerTg.Children.Add(new RotateTransform3D(_rotInnerX));
            innerTg.Children.Add(new RotateTransform3D(_rotInnerZ));
            innerModel.Transform = innerTg;
            masterGroup.Children.Add(innerModel);

            // 3C. Celestial Equatorial Golden Orbit Track Ring
            var trackMesh = GenerateTorusMesh(1.98, 0.08, 40, 12);
            var trackMat = new MaterialGroup();
            trackMat.Children.Add(new DiffuseMaterial(new LinearGradientBrush(
                Color.FromArgb(0xEE, 0xFF, 0xF7, 0xED),
                Color.FromArgb(0xCC, 0xFD, 0xBA, 0x74),
                new Point(0, 0), new Point(1, 1))));
            trackMat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xED, 0xD5)), 100.0));
            var trackModel = new GeometryModel3D(trackMesh, trackMat) { BackMaterial = trackMat };

            var trackTg = new Transform3DGroup();
            _rotTrackZ = new AxisAngleRotation3D(new Vector3D(0.3, 0.1, 1.0), 0);
            _rotTrackX = new AxisAngleRotation3D(new Vector3D(1.0, 0.0, 0.3), 64);
            trackTg.Children.Add(new RotateTransform3D(_rotTrackZ));
            trackTg.Children.Add(new RotateTransform3D(_rotTrackX));
            trackModel.Transform = trackTg;
            masterGroup.Children.Add(trackModel);

            // 3D. Orbiting Satellite Nodes (4 Procedural 3D Spheres in Harmonic Celestial Drift)
            _nodeTrans = new TranslateTransform3D[4];
            var sphereMesh = GenerateSphereMesh(0.18, 14, 14);

            for (int i = 0; i < 4; i++)
            {
                var sphereMat = new MaterialGroup();
                Color c1 = (i % 2 == 0) ? Color.FromRgb(0xFF, 0x92, 0x33) : Color.FromRgb(0xA8, 0x55, 0xF7);
                Color c2 = (i % 2 == 0) ? Color.FromRgb(0xEA, 0x58, 0x0C) : Color.FromRgb(0x7C, 0x3A, 0xED);
                sphereMat.Children.Add(new DiffuseMaterial(new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1))));
                sphereMat.Children.Add(new SpecularMaterial(Brushes.White, 90.0));

                var sphereModel = new GeometryModel3D(sphereMesh, sphereMat) { BackMaterial = sphereMat };
                _nodeTrans[i] = new TranslateTransform3D(0, 0, 0);
                sphereModel.Transform = _nodeTrans[i];
                masterGroup.Children.Add(sphereModel);
            }

            modelGroup.Children.Add(masterGroup);
            var visual = new ModelVisual3D { Content = modelGroup };
            _viewport.Children.Add(visual);
            Children.Add(_viewport);

            // 4. Interactive 3D Parallax Tracking via Parent Window Mouse Events
            Loaded += delegate
            {
                var win = Window.GetWindow(this);
                if (win != null)
                {
                    win.MouseMove += delegate(object sender, MouseEventArgs e)
                    {
                        Point pt = e.GetPosition(win);
                        if (win.ActualWidth > 0 && win.ActualHeight > 0)
                        {
                            _targetCamX = (pt.X / win.ActualWidth - 0.5) * 1.5;
                            _targetCamY = -(pt.Y / win.ActualHeight - 0.5) * 1.1;
                        }
                    };
                }
            };

            // 5. 60 FPS Three.js Dynamic PBR Animation Loop
            _stopwatch = Stopwatch.StartNew();
            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animTimer.Tick += delegate
            {
                double elapsed = _stopwatch.ElapsedMilliseconds / 1000.0;

                // Organic Floating Breath physics (Multi-harmonic sine wave)
                double floatY = Math.Sin(elapsed * 1.35) * 0.14 + Math.Cos(elapsed * 2.1) * 0.04;
                double floatZ = Math.Cos(elapsed * 1.1) * 0.08;
                _masterFloat.OffsetY = floatY;
                _masterFloat.OffsetZ = floatZ;

                // Outer Rotor continuous rotation + gyroscopic pitch wobble
                _rotOuterY.Angle = (_rotOuterY.Angle + 0.38) % 360.0;
                _rotOuterX.Angle = 16.0 + Math.Sin(elapsed * 0.8) * 8.0;

                // Inner Amethyst Rotor counter-rotation + harmonic roll
                _rotInnerY.Angle = (_rotInnerY.Angle - 0.60) % 360.0;
                _rotInnerX.Angle = -20.0 + Math.Cos(elapsed * 1.05) * 12.0;
                _rotInnerZ.Angle = Math.Sin(elapsed * 0.75) * 15.0;

                // Celestial Equatorial Track Ring tumbling roll
                _rotTrackZ.Angle = (elapsed * 22.0) % 360.0;
                _rotTrackX.Angle = 64.0 + Math.Sin(elapsed * 0.65) * 9.0;

                // 4 Orbiting Satellite Nodes (Harmonic 3D Lissajous Orbits)
                for (int k = 0; k < 4; k++)
                {
                    double nodeAngle = elapsed * 0.82 + (k * Math.PI / 2.0);
                    double orbitRadius = 2.65 + Math.Sin(elapsed * 1.4 + k) * 0.16;
                    double nx = orbitRadius * Math.Cos(nodeAngle);
                    double nz = orbitRadius * Math.Sin(nodeAngle);
                    double ny = Math.Sin(elapsed * 1.3 + k * 1.2) * 0.45;

                    _nodeTrans[k].OffsetX = nx;
                    _nodeTrans[k].OffsetY = ny;
                    _nodeTrans[k].OffsetZ = nz;
                }

                // Interactive 3D Camera Parallax interpolation
                _camX += (_targetCamX - _camX) * 0.05;
                _camY += (_targetCamY - _camY) * 0.05;
                _camera.Position = new Point3D(_camX, _camY, 8.2);
                _camera.LookDirection = new Vector3D(-_camX * 0.5, -_camY * 0.5, -8.2);

                // Dynamic Caustic Glow Drift
                _glow1Trans.X = Math.Sin(elapsed * 0.85) * 45.0;
                _glow1Trans.Y = Math.Cos(elapsed * 1.1) * 35.0;
                _glow2Trans.X = -Math.Cos(elapsed * 0.7) * 40.0;
                _glow2Trans.Y = -Math.Sin(elapsed * 0.95) * 30.0;
                _glow3Trans.X = Math.Cos(elapsed * 0.6) * 30.0;
                _glow3Trans.Y = Math.Sin(elapsed * 0.75) * 25.0;
            };
            _animTimer.Start();
        }

        private static MeshGeometry3D GenerateTorusMesh(double majorRadius, double minorRadius, int majorSegments, int minorSegments)
        {
            var mesh = new MeshGeometry3D();
            for (int i = 0; i <= majorSegments; i++)
            {
                double u = (double)i / majorSegments * 2.0 * Math.PI;
                double cosU = Math.Cos(u);
                double sinU = Math.Sin(u);

                for (int j = 0; j <= minorSegments; j++)
                {
                    double v = (double)j / minorSegments * 2.0 * Math.PI;
                    double cosV = Math.Cos(v);
                    double sinV = Math.Sin(v);

                    double x = (majorRadius + minorRadius * cosV) * cosU;
                    double y = (majorRadius + minorRadius * cosV) * sinU;
                    double z = minorRadius * sinV;

                    mesh.Positions.Add(new Point3D(x, y, z));
                    mesh.Normals.Add(new Vector3D(cosV * cosU, cosV * sinU, sinV));
                    mesh.TextureCoordinates.Add(new Point((double)i / majorSegments, (double)j / minorSegments));
                }
            }

            for (int i = 0; i < majorSegments; i++)
            {
                for (int j = 0; j < minorSegments; j++)
                {
                    int current = i * (minorSegments + 1) + j;
                    int next = (i + 1) * (minorSegments + 1) + j;

                    mesh.TriangleIndices.Add(current);
                    mesh.TriangleIndices.Add(next);
                    mesh.TriangleIndices.Add(current + 1);

                    mesh.TriangleIndices.Add(current + 1);
                    mesh.TriangleIndices.Add(next);
                    mesh.TriangleIndices.Add(next + 1);
                }
            }

            return mesh;
        }

        private static MeshGeometry3D GenerateSphereMesh(double radius, int latSegments, int lonSegments)
        {
            var mesh = new MeshGeometry3D();
            for (int lat = 0; lat <= latSegments; lat++)
            {
                double theta = (double)lat / latSegments * Math.PI;
                double sinTheta = Math.Sin(theta);
                double cosTheta = Math.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    double phi = (double)lon / lonSegments * 2.0 * Math.PI;
                    double sinPhi = Math.Sin(phi);
                    double cosPhi = Math.Cos(phi);

                    double x = radius * sinTheta * cosPhi;
                    double y = radius * cosTheta;
                    double z = radius * sinTheta * sinPhi;

                    mesh.Positions.Add(new Point3D(x, y, z));
                    mesh.Normals.Add(new Vector3D(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi));
                    mesh.TextureCoordinates.Add(new Point((double)lon / lonSegments, (double)lat / latSegments));
                }
            }

            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int first = lat * (lonSegments + 1) + lon;
                    int second = first + lonSegments + 1;

                    mesh.TriangleIndices.Add(first);
                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(first + 1);

                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(second + 1);
                    mesh.TriangleIndices.Add(first + 1);
                }
            }

            return mesh;
        }
    }

    public class SetupWindow : Window
    {
        private class GreetingItem
        {
            public string Language;
            public string Title;
            public string Subtitle;
            public Color ColorTop;
            public Color ColorBottom;
            public Color AccentColor;

            public GreetingItem(string lang, string title, string sub, Color top, Color btm, Color accent)
            {
                Language = lang;
                Title = title;
                Subtitle = sub;
                ColorTop = top;
                ColorBottom = btm;
                AccentColor = accent;
            }
        }

        // ======================================================================
        // TACTILE NEUMORPHIC TOGGLE SWITCH (CURD WHITE SOFT UI)
        // ======================================================================
        private class NeumorphicSwitch : Border
        {
            private bool _isChecked;
            private Border _track;
            private Border _thumb;
            private TranslateTransform _thumbTrans;
            private TextBlock _label;

            public bool IsChecked
            {
                get { return _isChecked; }
                set
                {
                    if (_isChecked != value)
                    {
                        _isChecked = value;
                        UpdateVisuals(true);
                    }
                }
            }

            public NeumorphicSwitch(string text, bool initialChecked)
            {
                _isChecked = initialChecked;
                Margin = new Thickness(0, 0, 0, 7);
                Cursor = Cursors.Hand;
                Background = Brushes.Transparent;

                var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Recessed Neumorphic Track (Curd White Groove)
                _track = new Border
                {
                    Width = 40,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    BorderThickness = new Thickness(1.2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var thumbContainer = new Grid
                {
                    Width = 40,
                    Height = 22,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                _thumbTrans = new TranslateTransform();
                _thumb = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = _thumbTrans,
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0xFF, 0xFF),
                        Color.FromRgb(0xEC, 0xE7, 0xDE),
                        new Point(0, 0), new Point(0, 1)),
                    BorderBrush = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0xFF, 0xFF),
                        Color.FromRgb(0xC6, 0xBD, 0xAC),
                        new Point(0, 0), new Point(0, 1)),
                    BorderThickness = new Thickness(1)
                };
                _thumb.Effect = new DropShadowEffect
                {
                    BlurRadius = 4,
                    ShadowDepth = 1.5,
                    Opacity = 0.40,
                    Color = Color.FromRgb(0x8C, 0x82, 0x72)
                };

                thumbContainer.Children.Add(_thumb);
                _track.Child = thumbContainer;
                Grid.SetColumn(_track, 0);
                row.Children.Add(_track);

                _label = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0x22, 0x30)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetColumn(_label, 1);
                row.Children.Add(_label);

                Child = row;

                MouseDown += delegate(object s, MouseButtonEventArgs e)
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        IsChecked = !IsChecked;
                    }
                };

                UpdateVisuals(false);
            }

            private void UpdateVisuals(bool animate)
            {
                double targetX = _isChecked ? 21.0 : 3.0;

                if (animate)
                {
                    var anim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    _thumbTrans.BeginAnimation(TranslateTransform.XProperty, anim);
                }
                else
                {
                    _thumbTrans.X = targetX;
                }

                if (_isChecked)
                {
                    // ON: Vibrant Bright Orange Neumorphic Track with glowing depth
                    _track.Background = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0x7A, 0x00),
                        Color.FromRgb(0xEA, 0x58, 0x0C),
                        new Point(0, 0), new Point(0, 1));
                    _track.BorderBrush = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0x9D, 0x42),
                        Color.FromRgb(0xC2, 0x41, 0x0C),
                        new Point(0, 0), new Point(0, 1));
                    _track.Effect = new DropShadowEffect
                    {
                        BlurRadius = 8,
                        ShadowDepth = 1,
                        Opacity = 0.45,
                        Color = Color.FromRgb(0xFF, 0x6B, 0x00)
                    };
                    _label.Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x22));
                    _label.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    // OFF: Soft recessed curd white groove
                    _track.Background = new LinearGradientBrush(
                        Color.FromRgb(0xE5, 0xDF, 0xD4),
                        Color.FromRgb(0xEE, 0xEA, 0xE2),
                        new Point(0, 0), new Point(0, 1));
                    _track.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xB8));
                    _track.Effect = null;
                    _label.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x72, 0x64));
                    _label.FontWeight = FontWeights.Normal;
                }
            }
        }

        private TextBox _txtInstallDir;
        private TextBlock _lblSpace;
        private NeumorphicSwitch _swDesktop;
        private NeumorphicSwitch _swStartMenu;
        private NeumorphicSwitch _swContext;
        private NeumorphicSwitch _swStartup;
        private NeumorphicSwitch _swLaunch;
        private Button _btnInstall;
        private Button _btnCancel;
        private ProgressBar _progressBar;
        private TextBlock _lblStatus;

        // Turning Drum Visuals
        private Border _drumBorder;
        private Grid _drumSlotA;
        private Grid _drumSlotB;
        private TranslateTransform _transA;
        private ScaleTransform _scaleA;
        private TranslateTransform _transB;
        private ScaleTransform _scaleB;

        private List<GreetingItem> _greetings;
        private int _greetingIndex = 0;
        private int _activeSlot = 0; // 0 = SlotA, 1 = SlotB
        private DispatcherTimer _drumTimer;

        // Bottom Illuminating Deck
        private Border _isuruDeck;
        private DropShadowEffect _isuruGlow;

        private bool _isInstalling = false;
        private bool _installFinished = false;
        private bool _installSuccess = false;

        public SetupWindow()
        {
            Title = "OMNIWHEEL 1.0 - Setup Wizard";
            Width = 580;
            Height = 770;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;

            InitGreetings();
            BuildUI();
            StartDrumRotation();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isInstalling && !_installFinished)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        private void InitGreetings()
        {
            _greetings = new List<GreetingItem>
            {
                new GreetingItem("English", "Welcome!", "Universal Radial Power Converter",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Sinhala", "ආයුබෝවන්!", "ඔබව සාදරයෙන් පිළිගනිමු • Universal Setup",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Tamil", "வணக்கம்!", "நல்வரவு • விரைவான கோப்பு மாற்றி",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Japanese", "ようこそ!", "ラジアル高速変換ユーティリティ • Windows",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("French", "Bienvenue !", "Convertisseur de fichiers radial pour Windows",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("German", "Willkommen!", "Universeller Radial-Konverter für Windows",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Spanish", "¡Bienvenido!", "Convertidor radial de alta velocidad",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Chinese", "欢迎!", "极速径向文件转换器 • Windows",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Arabic", "أهلاً وسهلاً!", "محول الملفات الشعاعي السريع • ويندوز",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Hindi", "नमस्ते!", "यूनिवर्सल रेडियल कन्वर्टर • विंडोज",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Italian", "Benvenuto!", "Convertitore radiale per Windows",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00)),
                new GreetingItem("Russian", "Добро пожаловать!", "Универсальный конвертер файлов",
                    Color.FromRgb(0x1A, 0x16, 0x24), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFF, 0x7A, 0x00))
            };
        }

        private void BuildUI()
        {
            // Outer Neumorphic Card Chassis with Realistic 3D PBR Glass Shell
            var rootBorder = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1.6),
                BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), // Top specular highlight
                    Color.FromArgb(0xC0, 0xC6, 0xBD, 0xAC), // Bottom warm shadow rim
                    new Point(0, 0), new Point(0, 1)),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xEE, 0xF7, 0xF4, 0xED),
                    Color.FromArgb(0xE8, 0xE7, 0xE1, 0xD4),
                    new Point(0, 0), new Point(0, 1))
            };
            rootBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 38,
                ShadowDepth = 12,
                Opacity = 0.45,
                Color = Color.FromRgb(0x78, 0x70, 0x62)
            };

            // Entrance Pop-In & Fade Animation
            var rootScale = new ScaleTransform(0.95, 0.95);
            rootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            rootBorder.RenderTransform = rootScale;
            Loaded += delegate
            {
                var popAnim = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }
                };
                var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280));
                rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
                rootBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });  // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });  // Footer

            // 1. Neumorphic Curd White Header
            var headerBorder = new Border
            {
                CornerRadius = new CornerRadius(17, 17, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xF2, 0xFC, 0xFA, 0xF6),
                    Color.FromArgb(0xE6, 0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 0, 0, 1.2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xD2, 0xCB, 0xBC)),
                Padding = new Thickness(22, 10, 18, 10)
            };
            headerBorder.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            var headerTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = "OMNIWHEEL  1.0  SETUP WIZARD",
                FontSize = 17,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x24)),
                FontFamily = new FontFamily("Segoe UI")
            };
            var subtitleBlock = new TextBlock
            {
                Text = "Radial Power Converter & Desktop Utility • Windows 7, 10 & 11",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x52, 0x44)),
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            headerTextStack.Children.Add(titleBlock);
            headerTextStack.Children.Add(subtitleBlock);
            Grid.SetColumn(headerTextStack, 0);
            headerGrid.Children.Add(headerTextStack);

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x4F, 0x44)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xB8)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            closeBtn.Effect = new DropShadowEffect
            {
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.30,
                Color = Color.FromRgb(0x99, 0x90, 0x80)
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(closeBtn);
            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            mainGrid.Children.Add(headerBorder);

            // 2. Main Content Body
            var contentStack = new StackPanel { Margin = new Thickness(24, 12, 24, 10) };

            // Standard Welcome description
            var introText = new TextBlock
            {
                Text = "Welcome to the OMNIWHEEL Setup Wizard. This wizard installs OMNIWHEEL 1.0 on your computer. Enjoy fast radial file conversions for 43+ formats directly inside Windows Explorer.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3E, 0x38, 0x2E)),
                LineHeight = 16.5,
                Margin = new Thickness(0, 0, 0, 10)
            };
            contentStack.Children.Add(introText);

            // TURNING DRUM VIEWPORT (Recessed Neumorphic Curd White Chamber)
            contentStack.Children.Add(CreateTurningDrumControl());

            // Destination Folder Box (Recessed Inset Plate in Curd White)
            var lblDir = new TextBlock
            {
                Text = "DESTINATION FOLDER:",
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                Margin = new Thickness(0, 10, 0, 4)
            };
            contentStack.Children.Add(lblDir);

            var dirGrid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            _txtInstallDir = new TextBox
            {
                Text = SetupApp.GetDefaultInstallDir(),
                Height = 34,
                Padding = new Thickness(10, 6, 10, 6),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xEB, 0xE6, 0xDC),
                    Color.FromRgb(0xF6, 0xF3, 0xED),
                    new Point(0, 0), new Point(0, 1)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xBD, 0xAC)),
                BorderThickness = new Thickness(1.2),
                FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _txtInstallDir.TextChanged += (s, e) => UpdateDiskSpaceLabel();
            Grid.SetColumn(_txtInstallDir, 0);
            dirGrid.Children.Add(_txtInstallDir);

            var btnBrowse = new Button
            {
                Content = "Browse...",
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEA, 0xE4, 0xD6),
                    new Point(0, 0), new Point(0, 1)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x20, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xC4, 0xB2)),
                BorderThickness = new Thickness(1.2),
                Cursor = Cursors.Hand
            };
            btnBrowse.Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.35,
                Color = Color.FromRgb(0x9E, 0x95, 0x85)
            };
            btnBrowse.Click += (s, e) =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.SelectedPath = _txtInstallDir.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _txtInstallDir.Text = Path.Combine(dlg.SelectedPath, "OMNIWHEEL");
                }
            };
            Grid.SetColumn(btnBrowse, 1);
            dirGrid.Children.Add(btnBrowse);
            contentStack.Children.Add(dirGrid);

            // Free disk space label
            _lblSpace = new TextBlock
            {
                Text = GetAvailableDiskSpace(_txtInstallDir.Text),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x58, 0x4A)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            contentStack.Children.Add(_lblSpace);

            // Integration Options
            var lblOpt = new TextBlock
            {
                Text = "INTEGRATION OPTIONS:",
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.Children.Add(lblOpt);

            _swDesktop = new NeumorphicSwitch("Create Desktop shortcut for OMNIWHEEL", true);
            _swStartMenu = new NeumorphicSwitch("Create Start Menu shortcut in Programs\\OMNIWHEEL", true);
            _swContext = new NeumorphicSwitch("Register Explorer Right-Click Context Menu (\"OMNIWHEEL (Donut)\")", true);
            _swStartup = new NeumorphicSwitch("Start automatically when Windows starts (Background service)", true);
            _swLaunch = new NeumorphicSwitch("Launch OMNIWHEEL immediately after installation completes", true);

            contentStack.Children.Add(_swDesktop);
            contentStack.Children.Add(_swStartMenu);
            contentStack.Children.Add(_swContext);
            contentStack.Children.Add(_swStartup);
            contentStack.Children.Add(_swLaunch);

            // Progress Bar & Status Readout
            _lblStatus = new TextBlock
            {
                Text = "Ready to install. Click 'Install Now' to proceed.",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0x32, 0x28)),
                Margin = new Thickness(0, 8, 0, 5)
            };
            contentStack.Children.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x85, 0x1A),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(1, 0)),
                Background = new SolidColorBrush(Color.FromRgb(0xD8, 0xD1, 0xC2)),
                BorderThickness = new Thickness(0)
            };
            contentStack.Children.Add(_progressBar);

            // BOTTOM ILLUMINATING "ISURU GUNASEKERA" DECK
            contentStack.Children.Add(CreateBottomIlluminatingDeck());

            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);

            // 3. Footer Bar with Cancel and Install Now buttons
            var footerBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 17, 17),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xEA, 0xEA, 0xE4, 0xD6),
                    Color.FromArgb(0xF4, 0xE2, 0xDC, 0xCE),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 1.2, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xCE, 0xC6, 0xB6)),
                Padding = new Thickness(22, 12, 22, 12)
            };
            var footerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _btnCancel = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 36,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 14, 0),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xF8, 0xF5, 0xEE),
                    Color.FromRgb(0xDF, 0xD8, 0xCA),
                    new Point(0, 0), new Point(0, 1)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x48, 0x3E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0xBC, 0xAC)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            _btnCancel.Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.25,
                Color = Color.FromRgb(0xA0, 0x98, 0x88)
            };
            _btnCancel.Click += (s, e) => Close();
            footerStack.Children.Add(_btnCancel);

            _btnInstall = new Button
            {
                Content = "Install Now",
                Width = 150,
                Height = 36,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x7A, 0x00),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x9D, 0x42),
                    Color.FromRgb(0xC2, 0x41, 0x0C),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(1.2),
                Cursor = Cursors.Hand
            };
            _btnInstall.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.55,
                Color = Color.FromRgb(0xFF, 0x6B, 0x00)
            };
            _btnInstall.Click += OnInstallClick;
            footerStack.Children.Add(_btnInstall);

            footerBorder.Child = footerStack;
            Grid.SetRow(footerBorder, 2);
            mainGrid.Children.Add(footerBorder);

            // Realistic Three.js 3D Background Layer + Interactive Foreground
            var masterGrid = new Grid();
            masterGrid.Children.Add(new RealisticThreeJsBackground(false));
            masterGrid.Children.Add(mainGrid);

            rootBorder.Child = masterGrid;
            Content = rootBorder;
        }

        // ======================================================================
        // TURNING DRUM GREETING STYLE COMPONENT (CURD WHITE NEUMORPHISM)
        // ======================================================================
        private Border CreateTurningDrumControl()
        {
            // Recessed Neumorphic Drum Chamber in Curd White
            _drumBorder = new Border
            {
                Height = 84,
                CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xDF, 0xD9, 0xCC),
                    Color.FromRgb(0xE8, 0xE2, 0xD6),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xB8, 0xB0, 0xA0), // Top crevice shadow
                    Color.FromRgb(0xFF, 0xFF, 0xFF), // Bottom specular highlight
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(1.4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ClipToBounds = true
            };
            _drumBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.35,
                Color = Color.FromRgb(0x9E, 0x95, 0x85)
            };

            var drumGrid = new Grid { ClipToBounds = true };

            // 1. Slot A
            _drumSlotA = CreateDrumSlot(out _transA, out _scaleA);
            _transA.Y = 0;
            _scaleA.ScaleY = 1.0;
            _drumSlotA.Opacity = 1.0;

            // 2. Slot B
            _drumSlotB = CreateDrumSlot(out _transB, out _scaleB);
            _transB.Y = 60;
            _scaleB.ScaleY = 0.4;
            _drumSlotB.Opacity = 0.0;

            drumGrid.Children.Add(_drumSlotA);
            drumGrid.Children.Add(_drumSlotB);

            // 3. Optical 3D Cylinder Curvature Vignette (Top shadow, Bottom shadow, Center specular line)
            var topShadow = new Border
            {
                Height = 22,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x40, 0x78, 0x70, 0x60),
                    Color.FromArgb(0x00, 0x78, 0x70, 0x60),
                    new Point(0, 0), new Point(0, 1)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(topShadow, 10);
            drumGrid.Children.Add(topShadow);

            var btmShadow = new Border
            {
                Height = 22,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x00, 0x78, 0x70, 0x60),
                    Color.FromArgb(0x40, 0x78, 0x70, 0x60),
                    new Point(0, 0), new Point(0, 1)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(btmShadow, 10);
            drumGrid.Children.Add(btmShadow);

            var horizonHighlight = new Border
            {
                Height = 1.2,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x00, 0xFF, 0x85, 0x1A),
                    Color.FromArgb(0x90, 0xFF, 0x85, 0x1A),
                    new Point(0, 0), new Point(1, 0)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(horizonHighlight, 11);
            drumGrid.Children.Add(horizonHighlight);

            // Initialize Slot A with first greeting
            PopulateSlot(_drumSlotA, _greetings[0]);

            _drumBorder.Child = drumGrid;
            return _drumBorder;
        }

        private Grid CreateDrumSlot(out TranslateTransform trans, out ScaleTransform scale)
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var tg = new TransformGroup();
            trans = new TranslateTransform();
            scale = new ScaleTransform();
            tg.Children.Add(scale);
            tg.Children.Add(trans);
            grid.RenderTransform = tg;

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Name = "GreetingTitle",
                FontSize = 28,
                FontWeight = FontWeights.ExtraBold,
                FontFamily = new FontFamily("Segoe UI, Iskoola Pota, Nirmala UI, Latha, Meiryo, Microsoft YaHei, Mangal, Tahoma, Arial"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(titleBlock);

            var subBlock = new TextBlock
            {
                Name = "GreetingSub",
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI, Iskoola Pota, Nirmala UI, Latha, Meiryo, Microsoft YaHei, Mangal, Tahoma, Arial"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x4E, 0x42)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };
            stack.Children.Add(subBlock);

            grid.Children.Add(stack);
            return grid;
        }

        private void PopulateSlot(Grid slot, GreetingItem item)
        {
            var stack = (StackPanel)slot.Children[0];
            var titleBlock = (TextBlock)stack.Children[0];
            var subBlock = (TextBlock)stack.Children[1];

            titleBlock.Text = item.Title;
            titleBlock.Foreground = new LinearGradientBrush(item.ColorTop, item.ColorBottom, new Point(0, 0), new Point(0, 1));
            titleBlock.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0xFF, 0xFF),
                ShadowDepth = 1,
                BlurRadius = 1,
                Opacity = 0.90
            };
            subBlock.Text = item.Subtitle;
        }

        private void StartDrumRotation()
        {
            _drumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
            _drumTimer.Tick += (s, e) => RotateDrumToNextLanguage();
            _drumTimer.Start();
        }

        private void RotateDrumToNextLanguage()
        {
            _greetingIndex = (_greetingIndex + 1) % _greetings.Count;
            GreetingItem nextItem = _greetings[_greetingIndex];

            Grid currentSlot = (_activeSlot == 0) ? _drumSlotA : _drumSlotB;
            Grid nextSlot = (_activeSlot == 0) ? _drumSlotB : _drumSlotA;
            TranslateTransform curTrans = (_activeSlot == 0) ? _transA : _transB;
            ScaleTransform curScale = (_activeSlot == 0) ? _scaleA : _scaleB;
            TranslateTransform nextTrans = (_activeSlot == 0) ? _transB : _transA;
            ScaleTransform nextScale = (_activeSlot == 0) ? _scaleB : _scaleA;

            PopulateSlot(nextSlot, nextItem);

            // Pre-position next slot at bottom of cylinder
            nextTrans.Y = 55;
            nextScale.ScaleY = 0.35;
            nextSlot.Opacity = 0.0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Roll current slot UP and over drum horizon
            var curYAnim = new DoubleAnimation(0, -55, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease };
            var curScaleAnim = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease };
            var curOpacAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(420));

            curTrans.BeginAnimation(TranslateTransform.YProperty, curYAnim);
            curScale.BeginAnimation(ScaleTransform.ScaleYProperty, curScaleAnim);
            currentSlot.BeginAnimation(UIElement.OpacityProperty, curOpacAnim);

            // Roll next slot IN from bottom to center
            var nextYAnim = new DoubleAnimation(55, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease };
            var nextScaleAnim = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease };
            var nextOpacAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(450));

            nextTrans.BeginAnimation(TranslateTransform.YProperty, nextYAnim);
            nextScale.BeginAnimation(ScaleTransform.ScaleYProperty, nextScaleAnim);
            nextSlot.BeginAnimation(UIElement.OpacityProperty, nextOpacAnim);

            _activeSlot = 1 - _activeSlot;
        }

        // ======================================================================
        // BOTTOM ILLUMINATING "ISURU GUNASEKERA" DECK (CURD WHITE NEUMORPHISM)
        // ======================================================================
        private Border CreateBottomIlluminatingDeck()
        {
            _isuruDeck = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1.4),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xBA, 0xB2, 0xA0), // Top crevice shadow
                    Color.FromRgb(0xFF, 0xFF, 0xFF), // Bottom specular highlight
                    new Point(0, 0), new Point(0, 1)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xE8, 0xE2, 0xD4),
                    Color.FromRgb(0xF0, 0xEB, 0xE0),
                    new Point(0, 0), new Point(0, 1)),
                Padding = new Thickness(16, 9, 16, 9),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Breathing / Pulsing Electric Orange Illumination Halo
            _isuruGlow = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 0, // Omnidirectional diffuse glow
                Opacity = 0.75,
                Color = Color.FromRgb(0xFF, 0x6B, 0x00)
            };
            _isuruDeck.Effect = _isuruGlow;

            // Breathing Illumination Animation
            var blurAnim = new DoubleAnimation(14, 28, TimeSpan.FromSeconds(1.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var opacAnim = new DoubleAnimation(0.55, 0.98, TimeSpan.FromSeconds(1.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            _isuruGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);
            _isuruGlow.BeginAnimation(DropShadowEffect.OpacityProperty, opacAnim);

            var mainStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var lettersStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            string text = "ISURU GUNASEKERA";
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                var tb = new TextBlock
                {
                    Text = c.ToString(),
                    FontSize = 24,
                    FontWeight = FontWeights.Black,
                    FontFamily = new FontFamily("Segoe UI, Arial Black"),
                    Margin = new Thickness(c == ' ' ? 8 : 1.0, 0, c == ' ' ? 8 : 1.0, 0),
                    Foreground = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0x85, 0x1A),
                        Color.FromRgb(0xEA, 0x58, 0x0C),
                        new Point(0, 0), new Point(0, 1))
                };
                tb.Effect = new DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.90,
                    Color = Color.FromRgb(0xFF, 0x9D, 0x42)
                };

                var tt = new TranslateTransform();
                tb.RenderTransform = tt;

                // Continuous Staggered Wave Jumping Animation
                var anim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(1.3),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 65)
                };
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.0)));
                anim.KeyFrames.Add(new SplineDoubleKeyFrame(-14, KeyTime.FromPercent(0.30))
                {
                    KeySpline = new KeySpline(0.2, 0.8, 0.2, 1.0)
                }); // Apex jump height
                anim.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0.60))
                {
                    KeySpline = new KeySpline(0.8, 0.2, 1.0, 0.8)
                });  // Touchdown bounce
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromPercent(0.75))); // Secondary bounce
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.90)));  // Settle
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));

                tt.BeginAnimation(TranslateTransform.YProperty, anim);
                lettersStack.Children.Add(tb);
            }

            var subText = new TextBlock
            {
                Text = "Architect & Creator • OMNIWHEEL 1.0",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x64, 0x42)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            mainStack.Children.Add(lettersStack);
            mainStack.Children.Add(subText);
            _isuruDeck.Child = mainStack;
            return _isuruDeck;
        }

        private void UpdateDiskSpaceLabel()
        {
            if (_lblSpace != null)
            {
                _lblSpace.Text = GetAvailableDiskSpace(_txtInstallDir != null ? _txtInstallDir.Text : null);
            }
        }

        private string GetAvailableDiskSpace(string targetDir)
        {
            try
            {
                if (string.IsNullOrEmpty(targetDir)) targetDir = SetupApp.GetDefaultInstallDir();
                string root = Path.GetPathRoot(Path.GetFullPath(targetDir));
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady)
                    {
                        double gb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        return string.Format("Space Required: ~85.0 MB   •   Space Available: {0:F1} GB on ({1})", gb, drive.Name.TrimEnd('\\'));
                    }
                }
            }
            catch { }
            return "Space Required: ~85.0 MB";
        }

        private void OnInstallClick(object sender, RoutedEventArgs e)
        {
            if (_installFinished)
            {
                // "Finish" clicked
                if (_swLaunch.IsChecked && _installSuccess)
                {
                    string targetDir = _txtInstallDir.Text.Trim();
                    string exePath = Path.Combine(targetDir, "OMNIWHEEL.exe");
                    if (File.Exists(exePath))
                    {
                        try { Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetDir }); } catch { }
                    }
                }
                Close();
                return;
            }

            _isInstalling = true;
            _btnInstall.IsEnabled = false;
            _btnCancel.IsEnabled = false;
            _txtInstallDir.IsEnabled = false;

            string installTarget = _txtInstallDir.Text.Trim();
            if (string.IsNullOrEmpty(installTarget)) installTarget = SetupApp.GetDefaultInstallDir();

            bool createDesktop = _swDesktop.IsChecked;
            bool createStartMenu = _swStartMenu.IsChecked;
            bool registerContext = _swContext.IsChecked;
            bool runStartup = _swStartup.IsChecked;

            // Speed up turning drum slightly during installation
            _drumTimer.Interval = TimeSpan.FromMilliseconds(1800);

            // Launch background installation thread
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = SetupApp.PerformInstall(installTarget, createDesktop, createStartMenu, registerContext, runStartup, false, true);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _installFinished = true;
                    _installSuccess = ok;
                }));
            });

            // Smooth Progress Bar Progression with Status Updates
            _lblStatus.Text = "Starting OMNIWHEEL Setup Wizard...";
            _progressBar.Value = 15;

            var stageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            int stage = 1;
            stageTimer.Tick += (s, ev) =>
            {
                if (stage == 1)
                {
                    _lblStatus.Text = "Unpacking application binaries and configuration...";
                    _progressBar.Value = 42;
                    stage = 2;
                }
                else if (stage == 2)
                {
                    _lblStatus.Text = "Extracting embedded FFmpeg multimedia conversion engine...";
                    _progressBar.Value = 72;
                    stage = 3;
                }
                else if (stage == 3)
                {
                    _lblStatus.Text = "Configuring Windows Explorer shell context menus & shortcuts...";
                    _progressBar.Value = 88;
                    stage = 4;
                }
                else if (stage == 4)
                {
                    if (_installFinished)
                    {
                        stageTimer.Stop();
                        _drumTimer.Interval = TimeSpan.FromMilliseconds(2400); // return to normal tempo
                        _progressBar.Value = 100;
                        if (_installSuccess)
                        {
                            _lblStatus.Text = "✓ Installation Complete! Press ` (Backtick) anywhere in Windows to summon.";
                            _btnInstall.Content = "Finish";
                            _btnInstall.IsEnabled = true;
                            _btnCancel.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            _lblStatus.Text = "Installation encountered an issue.";
                            _btnInstall.Content = "Retry";
                            _btnInstall.IsEnabled = true;
                            _btnCancel.IsEnabled = true;
                            _installFinished = false;
                        }
                    }
                }
            };
            stageTimer.Start();
        }
    }

    // ======================================================================
    // OMNIWHEEL UNINSTALL WINDOW (CURD WHITE NEUMORPHISM + SAD FACE)
    // ======================================================================
    public class UninstallWindow : Window
    {
        private ProgressBar _progressBar;
        private TextBlock _lblStatus;
        private Button _btnUninstall;
        private Button _btnCancel;
        private bool _isUninstalling = false;
        private bool _isFinished = false;

        public UninstallWindow()
        {
            Title = "OMNIWHEEL 1.0 - Uninstall";
            Width = 580;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;

            BuildUI();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isUninstalling && !_isFinished)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        private void BuildUI()
        {
            // Outer Neumorphic Card Chassis with Realistic 3D PBR Glass Shell
            var rootBorder = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1.6),
                BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), // Top specular highlight
                    Color.FromArgb(0xC0, 0xC6, 0xBD, 0xAC), // Bottom warm shadow rim
                    new Point(0, 0), new Point(0, 1)),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xEE, 0xF7, 0xF4, 0xED),
                    Color.FromArgb(0xE8, 0xE7, 0xE1, 0xD4),
                    new Point(0, 0), new Point(0, 1))
            };
            rootBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 38,
                ShadowDepth = 12,
                Opacity = 0.45,
                Color = Color.FromRgb(0x78, 0x70, 0x62)
            };

            // Entrance Pop-In & Fade Animation
            var rootScale = new ScaleTransform(0.95, 0.95);
            rootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            rootBorder.RenderTransform = rootScale;
            Loaded += delegate
            {
                var popAnim = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }
                };
                var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280));
                rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
                rootBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });

            // 1. Header Bar
            var headerBorder = new Border
            {
                CornerRadius = new CornerRadius(17, 17, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xF2, 0xFC, 0xFA, 0xF6),
                    Color.FromArgb(0xE6, 0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 0, 0, 1.2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xD2, 0xCB, 0xBC)),
                Padding = new Thickness(22, 10, 18, 10)
            };
            headerBorder.MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed) DragMove();
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            var headerTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = "OMNIWHEEL  1.0  UNINSTALL",
                FontSize = 17,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x24)),
                FontFamily = new FontFamily("Segoe UI")
            };
            var subtitleBlock = new TextBlock
            {
                Text = "Remove OMNIWHEEL and all associated desktop components",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x52, 0x44)),
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            headerTextStack.Children.Add(titleBlock);
            headerTextStack.Children.Add(subtitleBlock);
            Grid.SetColumn(headerTextStack, 0);
            headerGrid.Children.Add(headerTextStack);

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x4F, 0x44)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xB8)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            closeBtn.Click += delegate { Close(); };
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(closeBtn);
            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            mainGrid.Children.Add(headerBorder);

            // 2. Main Content Body
            var contentStack = new StackPanel { Margin = new Thickness(24, 12, 24, 10) };

            // 2A. SAD FACE IMAGE GRAPHIC
            var sadFaceContainer = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            sadFaceContainer.Child = CreateSadFaceGraphic();
            contentStack.Children.Add(sadFaceContainer);

            // 2B. EXACT USER TEXT: " SAAAAD TO SEE YOU GOOOO"
            var sadTitle = new TextBlock
            {
                Text = " SAAAAD TO SEE YOU GOOOO",
                FontSize = 25,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Segoe UI, Arial Black"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x7A, 0x00),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(1, 1)),
                Margin = new Thickness(0, 8, 0, 6)
            };
            sadTitle.Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 1,
                Opacity = 0.60,
                Color = Color.FromRgb(0xFF, 0x6B, 0x00)
            };
            contentStack.Children.Add(sadTitle);

            // 2C. Explanatory Message
            var messageBlock = new TextBlock
            {
                Text = "We're truly sad to see you leave! If you ran into any issues or have suggestions, we'd love your feedback. Clicking 'Uninstall OMNIWHEEL' below will cleanly remove all components from your system.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x46, 0x3A)),
                TextAlignment = TextAlignment.Center,
                LineHeight = 16.5,
                Margin = new Thickness(10, 0, 10, 10)
            };
            contentStack.Children.Add(messageBlock);

            // 2D. Scope Box (Recessed Inset Plate in Curd White)
            var scopeBox = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xEE, 0xEB, 0xE5, 0xD9),
                    Color.FromArgb(0xF6, 0xF5, 0xF2, 0xEC),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xC6, 0xBD, 0xAC),
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var scopeStack = new StackPanel();
            var scopeHeader = new TextBlock
            {
                Text = "WHAT WILL BE REMOVED:",
                FontSize = 11,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                Margin = new Thickness(0, 0, 0, 5)
            };
            scopeStack.Children.Add(scopeHeader);

            string[] items = new string[]
            {
                "• 43 universal file converters & native FFmpeg engine",
                "• Windows Explorer right-click context menu (\"OMNIWHEEL\")",
                "• Global radial shortcut (`) and Space bar Quick Preview hooks",
                "• Desktop and Start Menu program shortcuts",
                "• Windows auto-start background services and registry entries"
            };
            foreach (string it in items)
            {
                scopeStack.Children.Add(new TextBlock
                {
                    Text = it,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0x30, 0x28)),
                    Margin = new Thickness(0, 1.5, 0, 1.5)
                });
            }
            scopeBox.Child = scopeStack;
            contentStack.Children.Add(scopeBox);

            // 2E. Status Label & Progress Bar
            _lblStatus = new TextBlock
            {
                Text = "Ready to uninstall. Click 'Uninstall OMNIWHEEL' to proceed.",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0x32, 0x28)),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.Children.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x7A, 0x00),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(1, 0)),
                Background = new SolidColorBrush(Color.FromRgb(0xD8, 0xD1, 0xC2)),
                BorderThickness = new Thickness(0)
            };
            contentStack.Children.Add(_progressBar);

            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);

            // 3. Footer Bar with Keep App and Uninstall buttons
            var footerBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 17, 17),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xEA, 0xEA, 0xE4, 0xD6),
                    Color.FromArgb(0xF4, 0xE2, 0xDC, 0xCE),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 1.2, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xCE, 0xC6, 0xB6)),
                Padding = new Thickness(22, 12, 22, 12)
            };
            var footerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _btnCancel = new Button
            {
                Content = "Keep OMNIWHEEL",
                Width = 150,
                Height = 36,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 14, 0),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xF8, 0xF5, 0xEE),
                    Color.FromRgb(0xDF, 0xD8, 0xCA),
                    new Point(0, 0), new Point(0, 1)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x48, 0x3E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0xBC, 0xAC)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            _btnCancel.Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.25,
                Color = Color.FromRgb(0xA0, 0x98, 0x88)
            };
            _btnCancel.Click += delegate { Close(); };
            footerStack.Children.Add(_btnCancel);

            _btnUninstall = new Button
            {
                Content = "Uninstall OMNIWHEEL",
                Width = 185,
                Height = 36,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    Color.FromRgb(0xDC, 0x26, 0x26),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x85, 0x1A),
                    Color.FromRgb(0xB9, 0x1C, 0x1C),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(1.2),
                Cursor = Cursors.Hand
            };
            _btnUninstall.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.55,
                Color = Color.FromRgb(0xEA, 0x58, 0x0C)
            };
            _btnUninstall.Click += OnUninstallClick;
            footerStack.Children.Add(_btnUninstall);

            footerBorder.Child = footerStack;
            Grid.SetRow(footerBorder, 2);
            mainGrid.Children.Add(footerBorder);

            // Realistic Three.js 3D Background Layer + Interactive Foreground
            var masterGrid = new Grid();
            masterGrid.Children.Add(new RealisticThreeJsBackground(true));
            masterGrid.Children.Add(mainGrid);

            rootBorder.Child = masterGrid;
            Content = rootBorder;
        }

        private Canvas CreateSadFaceGraphic()
        {
            var canvas = new Canvas
            {
                Width = 112,
                Height = 112,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 1. Face Disc (Curd white with subtle warm shadow)
            var face = new System.Windows.Shapes.Ellipse
            {
                Width = 112,
                Height = 112,
                Fill = new LinearGradientBrush(
                    Color.FromRgb(0xFA, 0xF6, 0xEE),
                    Color.FromRgb(0xE6, 0xDF, 0xD0),
                    new Point(0, 0), new Point(0, 1)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xD0, 0xC6, 0xB4)),
                StrokeThickness = 2.2
            };
            face.Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.38,
                Color = Color.FromRgb(0x8A, 0x80, 0x70)
            };
            canvas.Children.Add(face);

            // Specular Arc on Top Forehead
            var specArc = new System.Windows.Shapes.Ellipse
            {
                Width = 84,
                Height = 36,
                Fill = new LinearGradientBrush(
                    Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                    new Point(0, 0), new Point(0, 1))
            };
            Canvas.SetLeft(specArc, 14);
            Canvas.SetTop(specArc, 8);
            canvas.Children.Add(specArc);

            // 2. Left Eyebrow (Tilted down inward in sadness: \)
            var leftBrow = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 28,34 Q 38,29 48,38"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x5A, 0x50, 0x42)),
                StrokeThickness = 3.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(leftBrow);

            // 3. Right Eyebrow (Tilted down inward in sadness: /)
            var rightBrow = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 64,38 Q 74,29 84,34"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x5A, 0x50, 0x42)),
                StrokeThickness = 3.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(rightBrow);

            // 4. Left Eye (Sad glossy oval)
            var leftEye = new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 17,
                Fill = new SolidColorBrush(Color.FromRgb(0x24, 0x1E, 0x2C))
            };
            Canvas.SetLeft(leftEye, 32);
            Canvas.SetTop(leftEye, 42);
            canvas.Children.Add(leftEye);

            // Left Eye Pupil Specular Highlight
            var leftSpec = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = Brushes.White };
            Canvas.SetLeft(leftSpec, 35);
            Canvas.SetTop(leftSpec, 44);
            canvas.Children.Add(leftSpec);

            var leftSpecSmall = new System.Windows.Shapes.Ellipse { Width = 2.5, Height = 2.5, Fill = Brushes.White };
            Canvas.SetLeft(leftSpecSmall, 34);
            Canvas.SetTop(leftSpecSmall, 51);
            canvas.Children.Add(leftSpecSmall);

            // 5. Right Eye (Sad glossy oval)
            var rightEye = new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 17,
                Fill = new SolidColorBrush(Color.FromRgb(0x24, 0x1E, 0x2C))
            };
            Canvas.SetLeft(rightEye, 68);
            Canvas.SetTop(rightEye, 42);
            canvas.Children.Add(rightEye);

            // Right Eye Pupil Specular Highlight
            var rightSpec = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = Brushes.White };
            Canvas.SetLeft(rightSpec, 71);
            Canvas.SetTop(rightSpec, 44);
            canvas.Children.Add(rightSpec);

            var rightSpecSmall = new System.Windows.Shapes.Ellipse { Width = 2.5, Height = 2.5, Fill = Brushes.White };
            Canvas.SetLeft(rightSpecSmall, 70);
            Canvas.SetTop(rightSpecSmall, 51);
            canvas.Children.Add(rightSpecSmall);

            // 6. Tear Drop falling from left eye (With Animated Gravity Translation & Opacity Pulse)
            var tear = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 38,60 C 34,65 31,69 31,74 C 31,79 35,83 40,83 C 45,83 49,79 49,74 C 49,69 45,65 41,60 Z"),
                Fill = new LinearGradientBrush(
                    Color.FromRgb(0x60, 0xA5, 0xFA),
                    Color.FromRgb(0x25, 0x63, 0xEB),
                    new Point(0, 0), new Point(0, 1)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
                StrokeThickness = 1.0
            };
            tear.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.50,
                Color = Color.FromRgb(0x3B, 0x82, 0xF6)
            };

            var tearTrans = new TranslateTransform();
            tear.RenderTransform = tearTrans;
            var tearDropAnim = new DoubleAnimation(0, 18, TimeSpan.FromSeconds(1.8))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            var tearFadeAnim = new DoubleAnimation(0.25, 0.95, TimeSpan.FromSeconds(1.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            tearTrans.BeginAnimation(TranslateTransform.YProperty, tearDropAnim);
            tear.BeginAnimation(UIElement.OpacityProperty, tearFadeAnim);

            canvas.Children.Add(tear);

            // 7. Sad Downturned Mouth (Pronounced sad curve with subtle quivering keyframe)
            var mouth = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 35,88 Q 56,70 77,88"),
                Stroke = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                StrokeThickness = 3.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            var mouthScale = new ScaleTransform(1.0, 1.0);
            mouth.RenderTransformOrigin = new Point(0.5, 0.5);
            mouth.RenderTransform = mouthScale;
            var quiverAnim = new DoubleAnimation(1.0, 1.05, TimeSpan.FromMilliseconds(500))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            mouthScale.BeginAnimation(ScaleTransform.ScaleXProperty, quiverAnim);

            canvas.Children.Add(mouth);

            return canvas;
        }

        private void OnUninstallClick(object sender, RoutedEventArgs e)
        {
            if (_isUninstalling) return;
            _isUninstalling = true;
            _btnCancel.IsEnabled = false;
            _btnUninstall.IsEnabled = false;

            var bw = new System.ComponentModel.BackgroundWorker();
            bw.WorkerReportsProgress = true;

            bw.DoWork += delegate(object s, System.ComponentModel.DoWorkEventArgs ea)
            {
                string targetDir = SetupApp.GetDefaultInstallDir();

                // 1. Terminate running instances
                bw.ReportProgress(15, "Terminating active OMNIWHEEL background processes...");
                System.Threading.Thread.Sleep(300);
                try
                {
                    foreach (var p in Process.GetProcessesByName("OMNIWHEEL"))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }

                // 2. Remove shortcuts
                bw.ReportProgress(35, "Removing Desktop and Start Menu shortcuts...");
                System.Threading.Thread.Sleep(250);
                try
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OMNIWHEEL.lnk");
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                    string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    string omniMenuDir = Path.Combine(programsDir, "OMNIWHEEL");
                    if (Directory.Exists(omniMenuDir)) Directory.Delete(omniMenuDir, true);
                }
                catch { }

                // 3. Remove context menu
                bw.ReportProgress(55, "Unregistering Explorer shell context menus...");
                System.Threading.Thread.Sleep(250);
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\OMNIWHEEL", false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\OMNIWHEEL", false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\OMNIWHEEL", false);
                }
                catch { }

                // 4. Remove startup and uninstall registry entries
                bw.ReportProgress(75, "Removing startup configuration and registry keys...");
                System.Threading.Thread.Sleep(250);
                try
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (runKey != null) runKey.DeleteValue("OMNIWHEEL", false);
                    }
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OMNIWHEEL", false);
                }
                catch { }

                // 5. Remove files and directory
                bw.ReportProgress(90, "Cleaning application binaries & FFmpeg engine...");
                System.Threading.Thread.Sleep(300);
                try
                {
                    if (Directory.Exists(targetDir))
                    {
                        foreach (string file in Directory.GetFiles(targetDir))
                        {
                            try
                            {
                                if (!Path.GetFileName(file).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Delete(file);
                                }
                            }
                            catch { }
                        }

                        string selfExe = Assembly.GetExecutingAssembly().Location;
                        bool isRunningFromTarget = !string.IsNullOrEmpty(selfExe) &&
                            selfExe.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase);

                        if (isRunningFromTarget)
                        {
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                                Arguments = string.Format("/C choice /C Y /N /D Y /T 2 & rmdir /s /q \"{0}\"", targetDir),
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);
                        }
                        else
                        {
                            Directory.Delete(targetDir, true);
                        }
                    }

                    // Remove Roaming AppData settings folder
                    string appDataSettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OMNIWHEEL");
                    if (Directory.Exists(appDataSettingsDir)) Directory.Delete(appDataSettingsDir, true);
                }
                catch { }

                bw.ReportProgress(100, "OMNIWHEEL has been completely removed from your system.");
                System.Threading.Thread.Sleep(400);
            };

            bw.ProgressChanged += delegate(object s, System.ComponentModel.ProgressChangedEventArgs ea)
            {
                _progressBar.Value = ea.ProgressPercentage;
                if (ea.UserState != null)
                {
                    _lblStatus.Text = ea.UserState.ToString();
                }
            };

            bw.RunWorkerCompleted += delegate
            {
                _isFinished = true;
                _isUninstalling = false;
                _lblStatus.Text = "OMNIWHEEL uninstalled successfully. Farewell!";
                _lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                _progressBar.Value = 100;

                _btnUninstall.Content = "Close";
                _btnUninstall.IsEnabled = true;
                _btnUninstall.Click -= OnUninstallClick;
                _btnUninstall.Click += delegate { Close(); };

                _btnCancel.Visibility = Visibility.Collapsed;
            };

            bw.RunWorkerAsync();
        }
    }
}
