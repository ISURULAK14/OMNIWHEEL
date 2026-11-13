using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;
using LocalConvertNative.UI;
using OMNIWHEEL.Installer;

namespace LocalConvertNative
{
    public class ScreenshotGenerator
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                var app = new Application();
                Console.WriteLine("Generating high-res UI screenshots...");
                string outDir = @"E:\ANTIGRAVITY\screenshots";
                Directory.CreateDirectory(outDir);

                // 1. Render Radial Donut Wheel (Front Wheel)
                try
                {
                    var wheelWin = new RadialDonutWindow();
                    wheelWin.ApplyTheme("Purple", "Light");
                    wheelWin.RenderToBitmap(Path.Combine(outDir, "omniwheel_front.png"));
                    Console.WriteLine("Captured: omniwheel_front.png");

                    // Back Wheel
                    var wheelBack = new RadialDonutWindow();
                    wheelBack.ApplyTheme("Purple", "Light");
                    wheelBack.FlipFace();
                    wheelBack.RenderToBitmap(Path.Combine(outDir, "omniwheel_back.png"));
                    Console.WriteLine("Captured: omniwheel_back.png");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error capturing wheel: " + ex.Message);
                }

                // 2. Render QuickLook Preview Window
                try
                {
                    var qlWin = new QuickLookWindow();
                    qlWin.ApplyTheme("Purple", "Light");
                    string sampleImg = @"E:\ANTIGRAVITY\Native\app.png";

                    qlWin.ShowPreview(new List<string> { sampleImg });
                    RenderWindow(qlWin, 920, 640, Path.Combine(outDir, "quicklook_preview.png"));
                    Console.WriteLine("Captured: quicklook_preview.png");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error capturing QuickLook: " + ex.Message);
                }

                // 3. Render Setup Installer Window
                try
                {
                    var setupWin = new SetupWindow();
                    RenderWindow(setupWin, 540, 700, Path.Combine(outDir, "setup_installer.png"));
                    Console.WriteLine("Captured: setup_installer.png");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error capturing SetupWindow: " + ex.Message);
                }

                // 4. Render Uninstaller Window
                try
                {
                    var uninstWin = new UninstallWindow();
                    RenderWindow(uninstWin, 520, 640, Path.Combine(outDir, "uninstaller.png"));
                    Console.WriteLine("Captured: uninstaller.png");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error capturing UninstallWindow: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal error: " + ex);
            }
        }

        private static void RenderWindow(Window win, int width, int height, string filePath)
        {
            UIElement target = (win.Content as UIElement) ?? win;
            target.Measure(new Size(width, height));
            target.Arrange(new Rect(0, 0, width, height));
            target.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(target);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(filePath))
            {
                enc.Save(fs);
            }
        }
    }
}
