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
            Width = 550;
            Height = 750;
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
            // Outer Neumorphic Card Chassis in Curd White (Soft UI Extrusion)
            var rootBorder = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF), // Top specular highlight
                    Color.FromRgb(0xC6, 0xBD, 0xAC), // Bottom warm shadow rim
                    new Point(0, 0), new Point(0, 1)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xF5, 0xF2, 0xEB), // Curd White top
                    Color.FromRgb(0xE7, 0xE2, 0xD6), // Curd White bottom
                    new Point(0, 0), new Point(0, 1))
            };
            rootBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 10,
                Opacity = 0.45,
                Color = Color.FromRgb(0x78, 0x70, 0x62)
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });  // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });  // Footer

            // 1. Neumorphic Curd White Header
            var headerBorder = new Border
            {
                CornerRadius = new CornerRadius(15, 15, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFC, 0xFA, 0xF6),
                    Color.FromRgb(0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 0, 0, 1.2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD2, 0xCB, 0xBC)),
                Padding = new Thickness(20, 10, 16, 10)
            };
            headerBorder.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            var headerTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = "OMNIWHEEL  1.0  SETUP WIZARD",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x1C, 0x2A)),
                FontFamily = new FontFamily("Segoe UI")
            };
            var subtitleBlock = new TextBlock
            {
                Text = "Radial Power Converter & Desktop Utility • Windows 7, 10 & 11",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x62, 0x5B, 0x4E)),
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
            var contentStack = new StackPanel { Margin = new Thickness(22, 12, 22, 10) };

            // Standard Welcome description
            var introText = new TextBlock
            {
                Text = "Welcome to the OMNIWHEEL Setup Wizard. This wizard installs OMNIWHEEL 1.0 on your computer. Enjoy fast radial file conversions for 43+ formats directly inside Windows Explorer.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x42, 0x38)),
                LineHeight = 16,
                Margin = new Thickness(0, 0, 0, 10)
            };
            contentStack.Children.Add(introText);

            // TURNING DRUM VIEWPORT (Recessed Neumorphic Curd White Chamber)
            contentStack.Children.Add(CreateTurningDrumControl());

            // Destination Folder Box (Recessed Inset Plate in Curd White)
            var lblDir = new TextBlock
            {
                Text = "DESTINATION FOLDER:",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                Margin = new Thickness(0, 10, 0, 4)
            };
            contentStack.Children.Add(lblDir);

            var dirGrid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });

            _txtInstallDir = new TextBox
            {
                Text = SetupApp.GetDefaultInstallDir(),
                Height = 30,
                Padding = new Thickness(8, 5, 8, 5),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xEB, 0xE6, 0xDC),
                    Color.FromRgb(0xF6, 0xF3, 0xED),
                    new Point(0, 0), new Point(0, 1)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xBD, 0xAC)),
                BorderThickness = new Thickness(1),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _txtInstallDir.TextChanged += (s, e) => UpdateDiskSpaceLabel();
            Grid.SetColumn(_txtInstallDir, 0);
            dirGrid.Children.Add(_txtInstallDir);

            var btnBrowse = new Button
            {
                Content = "Browse...",
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEA, 0xE4, 0xD6),
                    new Point(0, 0), new Point(0, 1)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x20, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xC4, 0xB2)),
                BorderThickness = new Thickness(1),
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
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x62, 0x54)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            contentStack.Children.Add(_lblSpace);

            // Integration Options
            var lblOpt = new TextBlock
            {
                Text = "INTEGRATION OPTIONS:",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
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
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x40, 0x34)),
                Margin = new Thickness(0, 8, 0, 5)
            };
            contentStack.Children.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Height = 8,
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
                CornerRadius = new CornerRadius(0, 0, 15, 15),
                Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xE4, 0xD6)),
                BorderThickness = new Thickness(0, 1.2, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCE, 0xC6, 0xB6)),
                Padding = new Thickness(20, 12, 20, 12)
            };
            var footerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _btnCancel = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 34,
                Margin = new Thickness(0, 0, 12, 0),
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
                Width = 145,
                Height = 34,
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
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.50,
                Color = Color.FromRgb(0xFF, 0x6B, 0x00)
            };
            _btnInstall.Click += OnInstallClick;
            footerStack.Children.Add(_btnInstall);

            footerBorder.Child = footerStack;
            Grid.SetRow(footerBorder, 2);
            mainGrid.Children.Add(footerBorder);

            rootBorder.Child = mainGrid;
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
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI, Iskoola Pota, Nirmala UI, Latha, Meiryo, Microsoft YaHei, Mangal, Tahoma, Arial"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(titleBlock);

            var subBlock = new TextBlock
            {
                Name = "GreetingSub",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI, Iskoola Pota, Nirmala UI, Latha, Meiryo, Microsoft YaHei, Mangal, Tahoma, Arial"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x58, 0x4A)),
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
                    FontSize = 23,
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
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.85,
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
            Width = 540;
            Height = 650;
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
            // Outer Neumorphic Card Chassis in Curd White
            var rootBorder = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xC6, 0xBD, 0xAC),
                    new Point(0, 0), new Point(0, 1)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xF5, 0xF2, 0xEB),
                    Color.FromRgb(0xE7, 0xE2, 0xD6),
                    new Point(0, 0), new Point(0, 1))
            };
            rootBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 10,
                Opacity = 0.45,
                Color = Color.FromRgb(0x78, 0x70, 0x62)
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(74) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });

            // 1. Header Bar
            var headerBorder = new Border
            {
                CornerRadius = new CornerRadius(15, 15, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFC, 0xFA, 0xF6),
                    Color.FromRgb(0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 0, 0, 1.2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD2, 0xCB, 0xBC)),
                Padding = new Thickness(20, 10, 16, 10)
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
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x1C, 0x2A)),
                FontFamily = new FontFamily("Segoe UI")
            };
            var subtitleBlock = new TextBlock
            {
                Text = "Remove OMNIWHEEL and all associated desktop components",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x62, 0x5B, 0x4E)),
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
            var contentStack = new StackPanel { Margin = new Thickness(24, 14, 24, 10) };

            // 2A. SAD FACE IMAGE GRAPHIC
            var sadFaceContainer = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 4)
            };
            sadFaceContainer.Child = CreateSadFaceGraphic();
            contentStack.Children.Add(sadFaceContainer);

            // 2B. EXACT USER TEXT: " SAAAAD TO SEE YOU GOOOO"
            var sadTitle = new TextBlock
            {
                Text = " SAAAAD TO SEE YOU GOOOO",
                FontSize = 23,
                FontWeight = FontWeights.ExtraBold,
                FontFamily = new FontFamily("Segoe UI, Arial Black"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x7A, 0x00),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(1, 1)),
                Margin = new Thickness(0, 10, 0, 6)
            };
            sadTitle.Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 1,
                Opacity = 0.55,
                Color = Color.FromRgb(0xFF, 0x6B, 0x00)
            };
            contentStack.Children.Add(sadTitle);

            // 2C. Explanatory Message
            var messageBlock = new TextBlock
            {
                Text = "We're truly sad to see you leave! If you ran into any issues or have suggestions, we'd love your feedback. Clicking 'Uninstall OMNIWHEEL' below will cleanly remove all components from your system.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x4E, 0x42)),
                TextAlignment = TextAlignment.Center,
                LineHeight = 16,
                Margin = new Thickness(10, 0, 10, 12)
            };
            contentStack.Children.Add(messageBlock);

            // 2D. Scope Box (Recessed Inset Plate in Curd White)
            var scopeBox = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xEB, 0xE5, 0xD9),
                    Color.FromRgb(0xF5, 0xF2, 0xEC),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xC6, 0xBD, 0xAC),
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var scopeStack = new StackPanel();
            var scopeHeader = new TextBlock
            {
                Text = "WHAT WILL BE REMOVED:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
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
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x38, 0x30)),
                    Margin = new Thickness(0, 1.5, 0, 1.5)
                });
            }
            scopeBox.Child = scopeStack;
            contentStack.Children.Add(scopeBox);

            // 2E. Status Label & Progress Bar
            _lblStatus = new TextBlock
            {
                Text = "Ready to uninstall. Click 'Uninstall OMNIWHEEL' to proceed.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x40, 0x34)),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.Children.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Height = 8,
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
                CornerRadius = new CornerRadius(0, 0, 15, 15),
                Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xE4, 0xD6)),
                BorderThickness = new Thickness(0, 1.2, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCE, 0xC6, 0xB6)),
                Padding = new Thickness(20, 12, 20, 12)
            };
            var footerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _btnCancel = new Button
            {
                Content = "Keep OMNIWHEEL",
                Width = 140,
                Height = 34,
                Margin = new Thickness(0, 0, 12, 0),
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
                Width = 175,
                Height = 34,
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
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.50,
                Color = Color.FromRgb(0xEA, 0x58, 0x0C)
            };
            _btnUninstall.Click += OnUninstallClick;
            footerStack.Children.Add(_btnUninstall);

            footerBorder.Child = footerStack;
            Grid.SetRow(footerBorder, 2);
            mainGrid.Children.Add(footerBorder);

            rootBorder.Child = mainGrid;
            Content = rootBorder;
        }

        private Canvas CreateSadFaceGraphic()
        {
            var canvas = new Canvas
            {
                Width = 100,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 1. Face Disc (Curd white with subtle warm shadow)
            var face = new System.Windows.Shapes.Ellipse
            {
                Width = 100,
                Height = 100,
                Fill = new LinearGradientBrush(
                    Color.FromRgb(0xFA, 0xF6, 0xEE),
                    Color.FromRgb(0xE6, 0xDF, 0xD0),
                    new Point(0, 0), new Point(0, 1)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xD0, 0xC6, 0xB4)),
                StrokeThickness = 2
            };
            face.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.35,
                Color = Color.FromRgb(0x8A, 0x80, 0x70)
            };
            canvas.Children.Add(face);

            // 2. Left Eyebrow (Tilted down inward in sadness: \)
            var leftBrow = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 26,30 Q 35,26 44,34"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x60, 0x54, 0x46)),
                StrokeThickness = 2.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(leftBrow);

            // 3. Right Eyebrow (Tilted down inward in sadness: /)
            var rightBrow = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 56,34 Q 65,26 74,30"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x60, 0x54, 0x46)),
                StrokeThickness = 2.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(rightBrow);

            // 4. Left Eye (Sad oval)
            var leftEye = new System.Windows.Shapes.Ellipse
            {
                Width = 11,
                Height = 15,
                Fill = new SolidColorBrush(Color.FromRgb(0x28, 0x22, 0x32))
            };
            Canvas.SetLeft(leftEye, 29);
            Canvas.SetTop(leftEye, 37);
            canvas.Children.Add(leftEye);

            // Left Eye Pupil Specular Highlight
            var leftSpec = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Fill = Brushes.White };
            Canvas.SetLeft(leftSpec, 32);
            Canvas.SetTop(leftSpec, 39);
            canvas.Children.Add(leftSpec);

            // 5. Right Eye (Sad oval)
            var rightEye = new System.Windows.Shapes.Ellipse
            {
                Width = 11,
                Height = 15,
                Fill = new SolidColorBrush(Color.FromRgb(0x28, 0x22, 0x32))
            };
            Canvas.SetLeft(rightEye, 60);
            Canvas.SetTop(rightEye, 37);
            canvas.Children.Add(rightEye);

            // Right Eye Pupil Specular Highlight
            var rightSpec = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Fill = Brushes.White };
            Canvas.SetLeft(rightSpec, 63);
            Canvas.SetTop(rightSpec, 39);
            canvas.Children.Add(rightSpec);

            // 6. Tear Drop falling from left eye
            var tear = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 34,54 C 30,59 28,63 28,67 C 28,72 32,76 36,76 C 40,76 44,72 44,67 C 44,63 40,59 36,54 Z"),
                Fill = new LinearGradientBrush(
                    Color.FromRgb(0x60, 0xA5, 0xFA),
                    Color.FromRgb(0x3B, 0x82, 0xF6),
                    new Point(0, 0), new Point(0, 1)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                StrokeThickness = 0.8
            };
            tear.Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.40,
                Color = Color.FromRgb(0x3B, 0x82, 0xF6)
            };
            canvas.Children.Add(tear);

            // 7. Sad Downturned Mouth (Pronounced sad curve)
            var mouth = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 32,78 Q 50,62 68,78"),
                Stroke = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                StrokeThickness = 3.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
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
