using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LocalConvertNative.Shell
{
    public static class StartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "OMNIWHEEL";
        private const string LegacyAppName = "LocalConvertPro";

        public static bool IsStartupRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppName);
                        if (val != null) return true;
                        var oldVal = key.GetValue(LegacyAppName);
                        if (oldVal != null) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool RegisterStartup()
        {
            try
            {
                string exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    exePath = Path.Combine(AppContext.BaseDirectory, "OMNIWHEEL.exe");
                    if (!File.Exists(exePath))
                    {
                        exePath = Path.Combine(AppContext.BaseDirectory, "LocalConvertNative.exe");
                    }
                }

                // 1. HKCU Run Registry Entry
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(AppName, string.Format("\"{0}\"", exePath));
                        key.DeleteValue(LegacyAppName, false);
                    }
                }

                // 2. Windows Startup Folder Shortcut
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    string oldShortcut = Path.Combine(startupFolder, "LocalConvert Pro.lnk");
                    if (File.Exists(oldShortcut)) File.Delete(oldShortcut);

                    string shortcutPath = Path.Combine(startupFolder, "OMNIWHEEL.lnk");
                    CreateShortcut(shortcutPath, exePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[StartupHelper] Register error: " + ex.Message);
                return false;
            }
        }

        public static bool UnregisterStartup()
        {
            try
            {
                // 1. Remove HKCU Run
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(AppName, false);
                        key.DeleteValue(LegacyAppName, false);
                    }
                }

                // 2. Remove Startup Shortcut
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "OMNIWHEEL.lnk");
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
                string oldShortcut = Path.Combine(startupFolder, "LocalConvert Pro.lnk");
                if (File.Exists(oldShortcut))
                {
                    File.Delete(oldShortcut);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[StartupHelper] Unregister error: " + ex.Message);
                return false;
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetExe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
                    shortcut.Description = "OMNIWHEEL Native Radial Quick Action Wheel";
                    shortcut.IconLocation = targetExe + ",0";
                    shortcut.Save();
                }
            }
            catch { }
        }
    }
}
