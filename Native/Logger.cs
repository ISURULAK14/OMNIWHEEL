using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LocalConvertNative
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;

        static Logger()
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logsDir = Path.Combine(localApp, "OMNIWHEEL", "logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }
                _logFilePath = Path.Combine(logsDir, "app_errors.log");
            }
            catch
            {
                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "OMNIWHEEL_logs");
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    _logFilePath = Path.Combine(tempDir, "app_errors.log");
                }
                catch
                {
                    _logFilePath = Path.Combine(Path.GetTempPath(), "app_errors.log");
                }
            }
        }

        public static string LogFilePath
        {
            get { return _logFilePath; }
        }

        public static void LogInfo(string message)
        {
            WriteEntry("INFO", message, null, null, -1);
        }

        public static void LogWarning(string message)
        {
            WriteEntry("WARN", message, null, null, -1);
        }

        public static void LogError(string message, Exception ex = null, string inputPath = null, long durationMs = -1)
        {
            WriteEntry("ERROR", message, ex, inputPath, durationMs);
        }

        private static void WriteEntry(string level, string message, Exception ex, string inputPath, long durationMs)
        {
            try
            {
                long memMb = 0;
                try
                {
                    memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                }
                catch { }

                var sb = new StringBuilder();
                sb.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [Mem: {2}MB] ", DateTime.Now, level, memMb);
                sb.Append(message);

                if (!string.IsNullOrEmpty(inputPath))
                {
                    long fSize = -1;
                    try { if (File.Exists(inputPath)) fSize = new FileInfo(inputPath).Length; } catch { }
                    sb.AppendFormat(" | Input: \"{0}\" (Size: {1} bytes)", Path.GetFileName(inputPath), fSize);
                }

                if (durationMs >= 0)
                {
                    sb.AppendFormat(" | Duration: {0}ms", durationMs);
                }

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.AppendFormat("Exception: {0}: {1}", ex.GetType().FullName, ex.Message);
                    sb.AppendLine();
                    sb.Append(ex.StackTrace);
                }

                sb.AppendLine();

                lock (_lock)
                {
                    // Rolling check: if > 10MB, roll to .old
                    if (File.Exists(_logFilePath))
                    {
                        var fi = new FileInfo(_logFilePath);
                        if (fi.Length > 10 * 1024 * 1024)
                        {
                            string oldPath = _logFilePath + ".old";
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(_logFilePath, oldPath);
                        }
                    }

                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception writeEx)
            {
                Debug.WriteLine("[Logger] Failed to write log: " + writeEx.Message);
            }
        }
    }
}
