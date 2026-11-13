using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LocalConvertNative
{
    public static class ShellNotificationHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, string dwItem2);

        private const int SHCNE_CREATE = 0x00000002;
        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_PATHW = 0x0005;

        public static void NotifyFileCreated(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW, filePath, null);
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, dir, null);
                }
            }
            catch { }
        }
    }
}
