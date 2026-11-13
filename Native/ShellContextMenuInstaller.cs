using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LocalConvertNative.Shell
{
    public static class ShellContextMenuInstaller
    {
        private static readonly string[] MenuKeyPaths = new string[]
        {
            @"Software\Classes\*\shell\OMNIWHEEL",
            @"Software\Classes\Directory\shell\OMNIWHEEL",
            @"Software\Classes\Directory\Background\shell\OMNIWHEEL"
        };

        private static readonly string[] LegacyMenuKeyPaths = new string[]
        {
            @"Software\Classes\*\shell\LocalConvertNative",
            @"Software\Classes\Directory\shell\LocalConvertNative",
            @"Software\Classes\Directory\Background\shell\LocalConvertNative"
        };

        public static bool RegisterContextMenu()
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

                // Clean legacy
                try
                {
                    foreach (var old in LegacyMenuKeyPaths)
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(old, false);
                    }
                }
                catch { }

                foreach (var path in MenuKeyPaths)
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
                                    {
                                        cmdKey.SetValue("", string.Format("\"{0}\" \"%V\"", exePath));
                                    }
                                    else
                                    {
                                        cmdKey.SetValue("", string.Format("\"{0}\" \"%1\"", exePath));
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ShellContextMenuInstaller] Register error: " + ex.Message);
                return false;
            }
        }

        public static bool UnregisterContextMenu()
        {
            try
            {
                foreach (var path in MenuKeyPaths)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(path, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ShellContextMenuInstaller] Unregister error: " + ex.Message);
                return false;
            }
        }
    }
}
