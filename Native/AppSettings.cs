using System;
using System.IO;
using System.Text;
using LocalConvertNative.Shell;

namespace LocalConvertNative
{
    public class AppSettings
    {
        // 1. Startup & System
        public bool StartWithWindows { get; set; }
        public bool OnlyInExplorer { get; set; }

        // 2. Hotkey & Interaction
        public string PrimaryHotkey { get; set; } // "Backtick", "CtrlSpace", "AltD", "F4"
        public string SummonMode { get; set; } // "Dual", "TapToggle", "PushToTalk"
        public bool EnableMouseWheelFlip { get; set; } // Flip layers on mouse wheel
        public bool EnableKeyFlip { get; set; } // Flip layers on Tab / Space / F
        public bool EnableSpacePeek { get; set; } // Spacebar Peek / QuickLook in File Explorer
        public string DefaultWheelLayer { get; set; } // "Primary" (converters) or "Secondary" (tools)

        // 3. Quality & Output Presets
        public int JpegQuality { get; set; }
        public string PngOptimization { get; set; }
        public string PdfPreset { get; set; }
        public string TargetDirectoryMode { get; set; } // "source" or "subfolder"

        // 4. Media Options
        public int AudioBitrate { get; set; } // 128, 192, 256, 320
        public int GifFramerate { get; set; } // 10, 15, 24

        // 5. Visual Theme & Feedback
        public string ThemeMode { get; set; } // "Dark" or "Light"
        public string ThemeAccent { get; set; } // "Purple", "DarkSlate", "Emerald", "Cobalt", "Amber"
        public string WheelScale { get; set; } // "Compact", "Standard", "Large"
        public bool ShowNotifications { get; set; }
        public bool PlaySoundOnComplete { get; set; }

        public AppSettings()
        {
            StartWithWindows = StartupHelper.IsStartupRegistered();
            OnlyInExplorer = true;

            PrimaryHotkey = "Backtick";
            SummonMode = "Dual";
            EnableMouseWheelFlip = true;
            EnableKeyFlip = true;
            EnableSpacePeek = true;
            DefaultWheelLayer = "Primary";

            JpegQuality = 92;
            PngOptimization = "Standard";
            PdfPreset = "Standard";
            TargetDirectoryMode = "source";

            AudioBitrate = 192;
            GifFramerate = 10;

            ThemeMode = "Dark";
            ThemeAccent = "Purple";
            WheelScale = "Standard";
            ShowNotifications = true;
            PlaySoundOnComplete = true;
        }

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "OMNIWHEEL");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string newFile = Path.Combine(dir, "settings.ini");
            string oldDir = Path.Combine(appData, "LocalConvertPro");
            string oldFile = Path.Combine(oldDir, "settings.ini");
            if (!File.Exists(newFile) && File.Exists(oldFile))
            {
                try { File.Copy(oldFile, newFile, true); } catch { }
            }

            return newFile;
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            string p = GetConfigPath();
            if (!File.Exists(p)) return s;

            try
            {
                var lines = File.ReadAllLines(p);
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    int idx = line.IndexOf('=');
                    if (idx < 0) continue;
                    string key = line.Substring(0, idx).Trim();
                    string val = line.Substring(idx + 1).Trim();

                    if (key.Equals("StartWithWindows", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.StartWithWindows = b;
                    }
                    else if (key.Equals("OnlyInExplorer", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.OnlyInExplorer = b;
                    }
                    else if (key.Equals("PrimaryHotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        s.PrimaryHotkey = val;
                    }
                    else if (key.Equals("SummonMode", StringComparison.OrdinalIgnoreCase))
                    {
                        s.SummonMode = val;
                    }
                    else if (key.Equals("EnableMouseWheelFlip", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.EnableMouseWheelFlip = b;
                    }
                    else if (key.Equals("EnableKeyFlip", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.EnableKeyFlip = b;
                    }
                    else if (key.Equals("EnableSpacePeek", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.EnableSpacePeek = b;
                    }
                    else if (key.Equals("DefaultWheelLayer", StringComparison.OrdinalIgnoreCase))
                    {
                        s.DefaultWheelLayer = val;
                    }
                    else if (key.Equals("JpegQuality", StringComparison.OrdinalIgnoreCase))
                    {
                        int q;
                        if (int.TryParse(val, out q)) s.JpegQuality = Math.Max(50, Math.Min(100, q));
                    }
                    else if (key.Equals("PngOptimization", StringComparison.OrdinalIgnoreCase))
                    {
                        s.PngOptimization = val;
                    }
                    else if (key.Equals("PdfPreset", StringComparison.OrdinalIgnoreCase))
                    {
                        s.PdfPreset = val;
                    }
                    else if (key.Equals("TargetDirectoryMode", StringComparison.OrdinalIgnoreCase))
                    {
                        s.TargetDirectoryMode = val;
                    }
                    else if (key.Equals("AudioBitrate", StringComparison.OrdinalIgnoreCase))
                    {
                        int ab;
                        if (int.TryParse(val, out ab)) s.AudioBitrate = ab;
                    }
                    else if (key.Equals("GifFramerate", StringComparison.OrdinalIgnoreCase))
                    {
                        int gf;
                        if (int.TryParse(val, out gf)) s.GifFramerate = gf;
                    }
                    else if (key.Equals("ThemeMode", StringComparison.OrdinalIgnoreCase))
                    {
                        s.ThemeMode = val;
                    }
                    else if (key.Equals("ThemeAccent", StringComparison.OrdinalIgnoreCase))
                    {
                        s.ThemeAccent = val;
                    }
                    else if (key.Equals("WheelScale", StringComparison.OrdinalIgnoreCase))
                    {
                        s.WheelScale = val;
                    }
                    else if (key.Equals("ShowNotifications", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.ShowNotifications = b;
                    }
                    else if (key.Equals("PlaySoundOnComplete", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) s.PlaySoundOnComplete = b;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            string p = GetConfigPath();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# OMNIWHEEL Settings");
                sb.AppendLine("StartWithWindows=" + StartWithWindows);
                sb.AppendLine("OnlyInExplorer=" + OnlyInExplorer);
                sb.AppendLine("PrimaryHotkey=" + PrimaryHotkey);
                sb.AppendLine("SummonMode=" + SummonMode);
                sb.AppendLine("EnableMouseWheelFlip=" + EnableMouseWheelFlip);
                sb.AppendLine("EnableKeyFlip=" + EnableKeyFlip);
                sb.AppendLine("EnableSpacePeek=" + EnableSpacePeek);
                sb.AppendLine("DefaultWheelLayer=" + DefaultWheelLayer);
                sb.AppendLine("JpegQuality=" + JpegQuality);
                sb.AppendLine("PngOptimization=" + PngOptimization);
                sb.AppendLine("PdfPreset=" + PdfPreset);
                sb.AppendLine("TargetDirectoryMode=" + TargetDirectoryMode);
                sb.AppendLine("AudioBitrate=" + AudioBitrate);
                sb.AppendLine("GifFramerate=" + GifFramerate);
                sb.AppendLine("ThemeMode=" + ThemeMode);
                sb.AppendLine("ThemeAccent=" + ThemeAccent);
                sb.AppendLine("WheelScale=" + WheelScale);
                sb.AppendLine("ShowNotifications=" + ShowNotifications);
                sb.AppendLine("PlaySoundOnComplete=" + PlaySoundOnComplete);
                File.WriteAllText(p, sb.ToString(), Encoding.UTF8);

                // Sync Windows Startup
                if (StartWithWindows)
                {
                    StartupHelper.RegisterStartup();
                }
                else
                {
                    StartupHelper.UnregisterStartup();
                }
            }
            catch { }
        }
    }
}
