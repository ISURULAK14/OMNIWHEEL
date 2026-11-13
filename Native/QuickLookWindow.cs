using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LocalConvertNative
{
    /// <summary>
    /// OMNIWHEEL Peek / QuickLook Window:
    /// High-performance instant borderless preview overlay activated via Space bar in Windows File Explorer.
    /// Supports Images (WIC), Source Code & Text with syntax highlighting, Markdown,
    /// PDF and Office Documents (via Shell Thumbnail Factory & OpenXML), Audio/Video playback,
    /// ZIP Archive inspection, Directory contents, and Binary Hex dump fallback.
    /// </summary>
    public class QuickLookWindow : Window
    {
        #region Win32 Shell & GDI Interop
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] int flags, [Out] out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern int SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("dwmapi.dll")]
        public static extern int DwmIsCompositionEnabled(out bool enabled);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        #endregion

        // Navigation state
        private List<string> _playlist = new List<string>();
        private int _currentIndex = 0;

        // UI Element References
        private Border _rootBorder;
        private Border _headerBorder;
        private Image _fileIconImage;
        private TextBlock _fileNameBlock;
        private Border _extBadge;
        private TextBlock _extBadgeText;
        private TextBlock _counterBlock;
        private Button _btnPrev;
        private Button _btnNext;
        private Button _btnOpen;
        private Button _btnFolder;
        private Button _btnClose;
        private Border _contentHost;
        private TextBlock _footerLeftText;
        private TextBlock _footerRightText;

        // Fluid Card Transform Group
        private ScaleTransform _cardScaleTransform;
        private TranslateTransform _slideTransform;
        private TransformGroup _cardTransformGroup;
        private bool _isClosing = false;
        private bool _isDarkMode = false;

        private Brush GetPrimaryTextBrush() { return _isDarkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x20, 0x1A, 0x2A)); }
        private Brush GetSecondaryTextBrush() { return _isDarkMode ? new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)) : new SolidColorBrush(Color.FromRgb(0x78, 0x70, 0x62)); }
        private Brush GetAccentSubTextBrush() { return _isDarkMode ? new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)) : new SolidColorBrush(Color.FromRgb(0x8A, 0x60, 0x40)); }
        private Brush GetChamberBgBrush() { return _isDarkMode ? new SolidColorBrush(Color.FromRgb(0x13, 0x10, 0x1D)) : new SolidColorBrush(Color.FromRgb(0xE2, 0xDC, 0xD0)); }
        private Brush GetChamberBorderBrush() { return _isDarkMode ? new SolidColorBrush(Color.FromRgb(0x38, 0x30, 0x48)) : new SolidColorBrush(Color.FromRgb(0xCE, 0xC6, 0xB6)); }

        // Media Element Tracker
        private MediaElement _activeMediaElement;
        private DispatcherTimer _mediaTimer;
        private Slider _mediaSeekSlider;
        private TextBlock _mediaTimeText;
        private Button _mediaPlayPauseBtn;
        private bool _isMediaPlaying = false;
        private bool _isUserDraggingSlider = false;
        private RotateTransform _activeVinylTransform;

        public QuickLookWindow()
        {
            Title = "OMNIWHEEL Peek";
            Width = 920;
            Height = 640;
            MinWidth = 540;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;

            BuildUI();
            ApplyTheme("Purple", "Dark");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            bool isDwm = true;
            try { DwmIsCompositionEnabled(out isDwm); } catch { }
        }

        private void BuildUI()
        {
            // Outer Neumorphic Floating Card with Hardware-Accelerated Transforms
            _cardScaleTransform = new ScaleTransform(1.0, 1.0);
            _slideTransform = new TranslateTransform(0, 0);
            _cardTransformGroup = new TransformGroup();
            _cardTransformGroup.Children.Add(_cardScaleTransform);
            _cardTransformGroup.Children.Add(_slideTransform);

            _rootBorder = new Border
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _cardTransformGroup,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1.4),
                BorderBrush = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xC6, 0xBD, 0xAC),
                    new Point(0, 0), new Point(0, 1)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xF6, 0xF3, 0xED),
                    Color.FromRgb(0xEA, 0xE4, 0xD6),
                    new Point(0, 0), new Point(0, 1))
            };
            _rootBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 32,
                ShadowDepth = 8,
                Opacity = 0.40,
                Color = Color.FromRgb(0x78, 0x70, 0x62)
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });  // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Viewport
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });  // Footer

            // 1. HEADER BAR
            _headerBorder = new Border
            {
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFC, 0xFA, 0xF6),
                    Color.FromRgb(0xEE, 0xE8, 0xDC),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD2, 0xCB, 0xBC)),
                Padding = new Thickness(14, 0, 14, 0)
            };
            _headerBorder.MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed) DragMove();
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1A. Left Info Cluster (Icon + Name + Ext Badge)
            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _fileIconImage = new Image
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftStack.Children.Add(_fileIconImage);

            _fileNameBlock = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x1C, 0x2A)),
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 360
            };
            leftStack.Children.Add(_fileNameBlock);

            _extBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x7A, 0x00),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(0, 1)),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _extBadgeText = new TextBlock
            {
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            };
            _extBadge.Child = _extBadgeText;
            leftStack.Children.Add(_extBadge);

            Grid.SetColumn(leftStack, 0);
            headerGrid.Children.Add(leftStack);

            // 1B. Center Navigation Cluster ([ < ] 4 of 18 [ > ])
            var centerNavStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };

            _btnPrev = CreateNavButton("‹", delegate { Navigate(-1); });
            _counterBlock = new TextBlock
            {
                Text = "1 of 1",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x58, 0x4A)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                FontFamily = new FontFamily("Segoe UI")
            };
            _btnNext = CreateNavButton("›", delegate { Navigate(1); });

            centerNavStack.Children.Add(_btnPrev);
            centerNavStack.Children.Add(_counterBlock);
            centerNavStack.Children.Add(_btnNext);

            Grid.SetColumn(centerNavStack, 1);
            headerGrid.Children.Add(centerNavStack);

            // 1C. Right Action Buttons (Open + Folder + Close)
            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _btnOpen = CreateActionButton("↗ Open", delegate { OpenInDefaultApp(); });
            _btnOpen.Margin = new Thickness(0, 0, 6, 0);
            rightStack.Children.Add(_btnOpen);

            _btnFolder = CreateActionButton("📁 Show", delegate { ShowInContainingFolder(); });
            _btnFolder.Margin = new Thickness(0, 0, 10, 0);
            rightStack.Children.Add(_btnFolder);

            _btnClose = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x4F, 0x44)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xB8)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            _btnClose.Click += delegate { ClosePreview(); };
            rightStack.Children.Add(_btnClose);

            Grid.SetColumn(rightStack, 2);
            headerGrid.Children.Add(rightStack);

            _headerBorder.Child = headerGrid;
            Grid.SetRow(_headerBorder, 0);
            mainGrid.Children.Add(_headerBorder);

            // 2. VIEWPORT / CONTENT HOST (Recessed Inset Chamber)
            _contentHost = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xEB, 0xE6, 0xDC),
                    Color.FromRgb(0xF5, 0xF2, 0xEB),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD0, 0xC0)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                ClipToBounds = true
            };
            Grid.SetRow(_contentHost, 1);
            mainGrid.Children.Add(_contentHost);

            // 3. FOOTER STATUS BAR
            var footerGrid = new Grid
            {
                Margin = new Thickness(16, 0, 16, 0)
            };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _footerLeftText = new TextBlock
            {
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x72, 0x6A, 0x5C)),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            Grid.SetColumn(_footerLeftText, 0);
            footerGrid.Children.Add(_footerLeftText);

            _footerRightText = new TextBlock
            {
                Text = "Space / Esc: Close  •  ← / →: Flip  •  Enter: Open",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            Grid.SetColumn(_footerRightText, 1);
            footerGrid.Children.Add(_footerRightText);

            Grid.SetRow(footerGrid, 2);
            mainGrid.Children.Add(footerGrid);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;
        }

        private Button CreateNavButton(string text, Action onClick)
        {
            var btn = new Button
            {
                Content = text,
                Width = 26,
                Height = 26,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x35, 0x2E, 0x40)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEB, 0xE5, 0xD8),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xB8)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btn.Click += delegate { onClick(); };
            return btn;
        }

        private Button CreateActionButton(string text, Action onClick)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(10, 4, 10, 4),
                Height = 28,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0x22, 0x30)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xEA, 0xE4, 0xD6),
                    new Point(0, 0), new Point(0, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xC4, 0xB2)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btn.Click += delegate { onClick(); };
            return btn;
        }

        #region Preview Activation & Dismissal
        public void ShowPreview(List<string> selectedFiles)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;

            // Stop any previous media
            CleanupActiveMedia();

            // Sibling detection if single file is highlighted
            if (selectedFiles.Count == 1)
            {
                string targetPath = selectedFiles[0];
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    try
                    {
                        var siblingList = new List<string>();
                        foreach (var f in Directory.EnumerateFiles(dir))
                        {
                            siblingList.Add(f);
                            if (siblingList.Count >= 2000) break; // Capped to avoid UI freeze on 10k+ file folders
                        }
                        siblingList.Sort(StringComparer.OrdinalIgnoreCase);
                        _playlist = siblingList;
                        _currentIndex = _playlist.IndexOf(targetPath);
                        if (_currentIndex < 0)
                        {
                            _playlist.Insert(0, targetPath);
                            _currentIndex = 0;
                        }
                    }
                    catch
                    {
                        _playlist = new List<string>(selectedFiles);
                        _currentIndex = 0;
                    }
                }
                else
                {
                    _playlist = new List<string>(selectedFiles);
                    _currentIndex = 0;
                }
            }
            else
            {
                _playlist = new List<string>(selectedFiles);
                _currentIndex = 0;
            }

            CenterOnActiveMonitor();
            LoadCurrentFile();

            _isClosing = false;
            _cardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _cardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _slideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, null);

            _cardScaleTransform.ScaleX = 0.93;
            _cardScaleTransform.ScaleY = 0.93;

            Opacity = 0.0;
            Show();
            Activate();
            Focus();

            var popAnimX = new DoubleAnimation(0.93, 1.0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            var popAnimY = new DoubleAnimation(0.93, 1.0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            _cardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, popAnimX);
            _cardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, popAnimY);

            var fadeAnim = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(120)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeAnim);
        }

        public void ClosePreview()
        {
            if (_isClosing) return;

            if (!IsVisible)
            {
                _isClosing = false;
                CleanupActiveMedia();
                if (_contentHost != null) _contentHost.Child = null;
                Hide();
                Program.TrimMemory();
                return;
            }

            _isClosing = true;

            // Smooth exit: shrink card & fade out in 80ms
            var exitScaleX = new DoubleAnimation(1.0, 0.94, new Duration(TimeSpan.FromMilliseconds(80)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var exitScaleY = new DoubleAnimation(1.0, 0.94, new Duration(TimeSpan.FromMilliseconds(80)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var exitFade = new DoubleAnimation(Opacity, 0.0, new Duration(TimeSpan.FromMilliseconds(80)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            exitFade.Completed += (s, e) =>
            {
                _isClosing = false;
                CleanupActiveMedia();
                if (_contentHost != null) _contentHost.Child = null;
                Hide();
                Program.TrimMemory();
            };

            _cardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, exitScaleX);
            _cardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, exitScaleY);
            BeginAnimation(OpacityProperty, exitFade);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            ClosePreview();
        }

        public void Navigate(int delta)
        {
            if (_playlist == null || _playlist.Count <= 1) return;

            CleanupActiveMedia();
            _currentIndex = (_currentIndex + delta + _playlist.Count) % _playlist.Count;
            LoadCurrentFile();

            // Smooth carousel horizontal slide
            double startX = delta > 0 ? 28.0 : -28.0;
            var slideAnim = new DoubleAnimation(startX, 0.0, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _slideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            if (_contentHost != null)
            {
                var flashAnim = new DoubleAnimation(0.70, 1.0, new Duration(TimeSpan.FromMilliseconds(130)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _contentHost.BeginAnimation(OpacityProperty, flashAnim);
            }
        }

        private void CenterOnActiveMonitor()
        {
            try
            {
                var mousePt = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(mousePt);
                var bounds = screen.WorkingArea;

                double scaleX = 1.0;
                double scaleY = 1.0;
                try
                {
                    var src = System.Windows.PresentationSource.FromVisual(this);
                    if (src != null && src.CompositionTarget != null)
                    {
                        scaleX = src.CompositionTarget.TransformToDevice.M11;
                        scaleY = src.CompositionTarget.TransformToDevice.M22;
                    }
                }
                catch { }
                if (scaleX <= 0.0) scaleX = 1.0;
                if (scaleY <= 0.0) scaleY = 1.0;

                double dipLeft = bounds.Left / scaleX;
                double dipTop = bounds.Top / scaleY;
                double dipWidth = bounds.Width / scaleX;
                double dipHeight = bounds.Height / scaleY;

                double targetW = Math.Min(980, dipWidth * 0.85);
                double targetH = Math.Min(680, dipHeight * 0.85);
                Width = targetW;
                Height = targetH;

                Left = dipLeft + (dipWidth - targetW) / 2.0;
                Top = dipTop + (dipHeight - targetH) / 2.0;
            }
            catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Space || e.Key == Key.Escape)
            {
                ClosePreview();
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.Up)
            {
                Navigate(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right || e.Key == Key.Down)
            {
                Navigate(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Return)
            {
                OpenInDefaultApp();
                e.Handled = true;
            }
        }
        #endregion

        #region Core File Presenters
        private void LoadCurrentFile()
        {
            if (_playlist == null || _playlist.Count == 0 || _currentIndex < 0 || _currentIndex >= _playlist.Count)
            {
                return;
            }

            string currentPath = _playlist[_currentIndex];
            bool isDir = Directory.Exists(currentPath);
            bool isFile = File.Exists(currentPath);

            if (!isDir && !isFile)
            {
                _fileNameBlock.Text = "File Not Found: " + Path.GetFileName(currentPath);
                _extBadgeText.Text = "ERR";
                _contentHost.Child = CreateErrorViewer("File or folder no longer exists at:\n" + currentPath);
                return;
            }

            string fileName = Path.GetFileName(currentPath);
            if (string.IsNullOrEmpty(fileName)) fileName = currentPath;
            string ext = isDir ? "DIR" : Path.GetExtension(currentPath).ToLowerInvariant();

            _fileNameBlock.Text = fileName;
            _fileNameBlock.ToolTip = currentPath;
            _extBadgeText.Text = string.IsNullOrEmpty(ext) ? "FILE" : ext.TrimStart('.').ToUpperInvariant();

            // Sibling counter
            if (_playlist.Count > 1)
            {
                _counterBlock.Text = string.Format("{0} of {1}", _currentIndex + 1, _playlist.Count);
                _btnPrev.IsEnabled = true;
                _btnNext.IsEnabled = true;
            }
            else
            {
                _counterBlock.Text = "1 of 1";
                _btnPrev.IsEnabled = false;
                _btnNext.IsEnabled = false;
            }

            // Shell Icon
            UpdateShellIcon(currentPath);

            // Presenter Routing
            UIElement presenter = null;
            string footerDetails = "";

            if (isDir)
            {
                presenter = CreateFolderViewer(currentPath, out footerDetails);
            }
            else
            {
                long fileSize = 0;
                DateTime lastModified = DateTime.Now;
                try
                {
                    var fi = new FileInfo(currentPath);
                    fileSize = fi.Length;
                    lastModified = fi.LastWriteTime;
                }
                catch { }

                footerDetails = string.Format("{0}  •  Modified: {1:yyyy-MM-dd HH:mm:ss}", FormatBytes(fileSize), lastModified);

                if (IsImageExtension(ext))
                {
                    presenter = CreateImageViewer(currentPath, ref footerDetails);
                }
                else if (IsVideoExtension(ext))
                {
                    presenter = CreateMediaViewer(currentPath, true, ref footerDetails);
                }
                else if (IsAudioExtension(ext))
                {
                    presenter = CreateMediaViewer(currentPath, false, ref footerDetails);
                }
                else if (IsCodeOrTextExtension(ext))
                {
                    bool isMarkdown = ext == ".md" || ext == ".markdown";
                    presenter = CreateCodeViewer(currentPath, isMarkdown, ref footerDetails);
                }
                else if (IsDocumentExtension(ext))
                {
                    presenter = CreateDocumentViewer(currentPath, ext, ref footerDetails);
                }
                else if (IsArchiveExtension(ext))
                {
                    presenter = CreateArchiveViewer(currentPath, ref footerDetails);
                }
                else
                {
                    // Fallback to high-res Shell Thumbnail or Binary Hex dump
                    presenter = CreateFallbackViewer(currentPath, ref footerDetails);
                }
            }

            if (presenter != null)
            {
                var fadeAnim = new DoubleAnimation(0.2, 1.0, new Duration(TimeSpan.FromMilliseconds(100)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                presenter.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }
            _contentHost.Child = presenter;
            _footerLeftText.Text = footerDetails;
        }

        private void UpdateShellIcon(string path)
        {
            try
            {
                SHFILEINFO shinfo = new SHFILEINFO();
                IntPtr hImg = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                if (shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        ImageSource iconSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        _fileIconImage.Source = iconSource;
                        return;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }
            _fileIconImage.Source = null;
        }

        // 1. IMAGE PRESENTER (WIC High-DPI Engine)
        private UIElement CreateImageViewer(string path, ref string footerDetails)
        {
            var grid = new Grid();

            try
            {
                BitmapSource imgSource = null;
                int pixelW = 0, pixelH = 0;
                string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

                // 1. Fast path for standard GDI+/WIC formats (PNG, JPG, BMP, GIF, TIFF, ICO)
                if (ext != "webp" && ext != "heic" && ext != "avif" && ext != "svg")
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        imgSource = bmp;
                        pixelW = bmp.PixelWidth;
                        pixelH = bmp.PixelHeight;
                    }
                    catch { }
                }

                // 2. Comprehensive decoding fallback (WebP, HEIC, AVIF, SVG, Exif orientation)
                if (imgSource == null)
                {
                    using (var gdiBmp = Engine.FileConversionEngine.LoadImageSafely(path))
                    {
                        if (gdiBmp != null)
                        {
                            pixelW = gdiBmp.Width;
                            pixelH = gdiBmp.Height;
                            IntPtr hBmp = gdiBmp.GetHbitmap();
                            try
                            {
                                imgSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                imgSource.Freeze();
                            }
                            finally
                            {
                                DeleteObject(hBmp);
                            }
                        }
                    }
                }

                if (imgSource == null)
                {
                    return CreateErrorViewer("Unsupported or corrupted image file.");
                }

                var img = new Image
                {
                    Source = imgSource,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);

                // Resolution Badge
                var resPill = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x1A, 0x16, 0x24)),
                    Padding = new Thickness(8, 3, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 12, 14, 0)
                };
                resPill.Child = new TextBlock
                {
                    Text = string.Format("{0} × {1} px", pixelW, pixelH),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI")
                };
                grid.Children.Add(resPill);

                footerDetails = string.Format("{0} × {1} px  •  {2}", pixelW, pixelH, footerDetails);
            }
            catch (Exception ex)
            {
                return CreateErrorViewer("Could not load image: " + ex.Message);
            }

            return grid;
        }

        // 2. CODE & TEXT PRESENTER (Syntax Tokenizer + Line Number Gutter + Markdown)
        private UIElement CreateCodeViewer(string path, bool isMarkdown, ref string footerDetails)
        {
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            string content = "";
            int lineCount = 0;
            try
            {
                using (var reader = new StreamReader(path, Encoding.Default, true))
                {
                    char[] buffer = new char[64 * 1024]; // 64 KB cap (avoids Large Object Heap fragmentation)
                    int read = reader.Read(buffer, 0, buffer.Length);
                    content = new string(buffer, 0, read);
                }
            }
            catch (Exception ex)
            {
                return CreateErrorViewer("Could not read text: " + ex.Message);
            }

            string[] lines = content.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            lineCount = lines.Length;

            footerDetails = string.Format("{0} lines  •  {1} chars  •  {2}", lineCount, content.Length, footerDetails);

            // Sub-header for Code info & Markdown view switcher
            var subHeader = new Border
            {
                Background = GetChamberBgBrush(),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = GetChamberBorderBrush(),
                Padding = new Thickness(14, 4, 14, 4)
            };
            var subHeaderGrid = new Grid();
            var statsText = new TextBlock
            {
                Text = string.Format("UTF-8  •  {0} lines  •  {1} characters", lineCount, content.Length),
                FontSize = 10,
                Foreground = GetSecondaryTextBrush(),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            subHeaderGrid.Children.Add(statsText);
            subHeader.Child = subHeaderGrid;

            Grid.SetRow(subHeader, 0);
            rootGrid.Children.Add(subHeader);

            // Two-column Scrollable Editor View (Gutter + Code)
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8)
            };

            var editorGrid = new Grid();
            editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Line numbers column
            var sbGutter = new StringBuilder();
            for (int i = 1; i <= lineCount; i++)
            {
                sbGutter.AppendLine(i.ToString());
            }

            var gutterBlock = new TextBlock
            {
                Text = sbGutter.ToString(),
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New"),
                Foreground = GetSecondaryTextBrush(),
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(4, 0, 14, 0),
                LineHeight = 18,
                IsHitTestVisible = false
            };
            Grid.SetColumn(gutterBlock, 0);
            editorGrid.Children.Add(gutterBlock);

            // Code Text Block with Syntax Highlighting
            var codeBlock = new TextBlock
            {
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New"),
                LineHeight = 18
            };

            // Syntax tokenize lines
            HighlightSourceCode(codeBlock, lines);

            Grid.SetColumn(codeBlock, 1);
            editorGrid.Children.Add(codeBlock);

            scroll.Content = editorGrid;
            Grid.SetRow(scroll, 1);
            rootGrid.Children.Add(scroll);

            return rootGrid;
        }

        private static readonly HashSet<string> _keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "class", "interface", "struct", "enum",
            "static", "void", "string", "int", "bool", "double", "float", "var", "let", "const",
            "return", "if", "else", "for", "foreach", "while", "do", "switch", "case", "break",
            "continue", "try", "catch", "finally", "throw", "new", "using", "namespace", "import",
            "from", "export", "default", "function", "def", "async", "await", "SELECT", "FROM", "WHERE",
            "true", "false", "null", "undefined", "this", "base"
        };

        private static readonly SolidColorBrush _kwBrush = CreateFrozenSolidBrush(0xEA, 0x58, 0x0C); // Bright Flame Orange
        private static readonly SolidColorBrush _strBrush = CreateFrozenSolidBrush(0x16, 0xA3, 0x4A); // Forest Green
        private static readonly SolidColorBrush _commentBrush = CreateFrozenSolidBrush(0x78, 0x70, 0x62); // Warm Gray Italic
        private static readonly SolidColorBrush _numBrush = CreateFrozenSolidBrush(0xD9, 0x77, 0x06); // Amber
        private static readonly SolidColorBrush _normalBrush = CreateFrozenSolidBrush(0x20, 0x1A, 0x2A);

        private static readonly SolidColorBrush _kwBrushDark = CreateFrozenSolidBrush(0xFB, 0x92, 0x3C); // Radiant Orange
        private static readonly SolidColorBrush _strBrushDark = CreateFrozenSolidBrush(0x4A, 0xDE, 0x80); // Radiant Mint
        private static readonly SolidColorBrush _commentBrushDark = CreateFrozenSolidBrush(0x94, 0xA3, 0xB8); // Crisp Slate Gray Italic
        private static readonly SolidColorBrush _numBrushDark = CreateFrozenSolidBrush(0xFD, 0xBA, 0x74); // Radiant Amber
        private static readonly SolidColorBrush _normalBrushDark = CreateFrozenSolidBrush(0xF8, 0xFA, 0xFC); // Pure Crisp Luminous White

        private static SolidColorBrush CreateFrozenSolidBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private void HighlightSourceCode(TextBlock targetBlock, string[] lines)
        {
            SolidColorBrush kwBrush = _isDarkMode ? _kwBrushDark : _kwBrush;
            SolidColorBrush strBrush = _isDarkMode ? _strBrushDark : _strBrush;
            SolidColorBrush commentBrush = _isDarkMode ? _commentBrushDark : _commentBrush;
            SolidColorBrush numBrush = _isDarkMode ? _numBrushDark : _numBrush;
            SolidColorBrush normalBrush = _isDarkMode ? _normalBrushDark : _normalBrush;

            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                if (string.IsNullOrEmpty(line))
                {
                    targetBlock.Inlines.Add(new Run("\n"));
                    continue;
                }

                // Check whole-line comment
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("#") || trimmed.StartsWith("<!--") || trimmed.StartsWith("/*"))
                {
                    var r = new Run(line + "\n") { Foreground = commentBrush, FontStyle = FontStyles.Italic };
                    targetBlock.Inlines.Add(r);
                    continue;
                }

                // Fast Tokenize by whitespace/punctuation
                int i = 0;
                while (i < line.Length)
                {
                    char c = line[i];

                    // String literal
                    if (c == '"' || c == '\'')
                    {
                        char quote = c;
                        int start = i++;
                        while (i < line.Length && line[i] != quote)
                        {
                            if (line[i] == '\\' && i + 1 < line.Length) i++;
                            i++;
                        }
                        if (i < line.Length) i++;
                        targetBlock.Inlines.Add(new Run(line.Substring(start, i - start)) { Foreground = strBrush });
                        continue;
                    }

                    // Comment start mid-line
                    if ((c == '/' && i + 1 < line.Length && line[i + 1] == '/') || c == '#')
                    {
                        targetBlock.Inlines.Add(new Run(line.Substring(i)) { Foreground = commentBrush, FontStyle = FontStyles.Italic });
                        i = line.Length;
                        break;
                    }

                    // Word / Keyword / Number
                    if (char.IsLetterOrDigit(c) || c == '_')
                    {
                        int start = i++;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                        {
                            i++;
                        }
                        string word = line.Substring(start, i - start);
                        if (_keywords.Contains(word))
                        {
                            targetBlock.Inlines.Add(new Run(word) { Foreground = kwBrush, FontWeight = FontWeights.Bold });
                        }
                        else if (char.IsDigit(word[0]))
                        {
                            targetBlock.Inlines.Add(new Run(word) { Foreground = numBrush });
                        }
                        else
                        {
                            targetBlock.Inlines.Add(new Run(word) { Foreground = normalBrush });
                        }
                        continue;
                    }

                    // Other single char
                    targetBlock.Inlines.Add(new Run(c.ToString()) { Foreground = normalBrush });
                    i++;
                }

                targetBlock.Inlines.Add(new Run("\n"));
            }
        }

        // 3. MEDIA PRESENTER (Video & Audio Player)
        private UIElement CreateMediaViewer(string path, bool isVideo, ref string footerDetails)
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); // Playback Bar

            _activeMediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Source = new Uri(path, UriKind.Absolute),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10)
            };

            if (isVideo)
            {
                _activeMediaElement.MouseDown += delegate(object s, MouseButtonEventArgs e)
                {
                    ToggleMediaPlayPause();
                };
                Grid.SetRow(_activeMediaElement, 0);
                mainGrid.Children.Add(_activeMediaElement);
            }
            else
            {
                // Audio Vinyl Artwork Panel
                var audioPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var vinylDisc = new Border
                {
                    Width = 140,
                    Height = 140,
                    CornerRadius = new CornerRadius(70),
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x22, 0x1E, 0x2A),
                        Color.FromRgb(0x10, 0x0E, 0x16),
                        new Point(0, 0), new Point(1, 1)),
                    BorderThickness = new Thickness(4),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                vinylDisc.RenderTransformOrigin = new Point(0.5, 0.5);
                var vinylTransform = new RotateTransform();
                vinylDisc.RenderTransform = vinylTransform;
                _activeVinylTransform = vinylTransform;

                var spinAnim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3.5)))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                _activeVinylTransform.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
                vinylDisc.Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.50,
                    Color = Color.FromRgb(0xEA, 0x58, 0x0C)
                };

                var centerHole = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x00)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                centerHole.Child = new TextBlock
                {
                    Text = "♪",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                vinylDisc.Child = centerHole;
                audioPanel.Children.Add(vinylDisc);

                var audioTitle = new TextBlock
                {
                    Text = Path.GetFileName(path),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = GetPrimaryTextBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 4),
                    FontFamily = new FontFamily("Segoe UI")
                };
                audioPanel.Children.Add(audioTitle);

                Grid.SetRow(audioPanel, 0);
                mainGrid.Children.Add(audioPanel);
                mainGrid.Children.Add(_activeMediaElement);
            }

            // Playback Bar
            var barBorder = new Border
            {
                Background = GetChamberBgBrush(),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = GetChamberBorderBrush(),
                Padding = new Thickness(14, 0, 14, 0)
            };
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _mediaPlayPauseBtn = new Button
            {
                Content = "▶",
                Width = 32,
                Height = 30,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x7A, 0x00),
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    new Point(0, 0), new Point(0, 1)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            _mediaPlayPauseBtn.Click += delegate { ToggleMediaPlayPause(); };
            Grid.SetColumn(_mediaPlayPauseBtn, 0);
            barGrid.Children.Add(_mediaPlayPauseBtn);

            _mediaSeekSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 14, 0),
                Cursor = Cursors.Hand
            };
            _mediaSeekSlider.PreviewMouseDown += delegate { _isUserDraggingSlider = true; };
            _mediaSeekSlider.PreviewMouseUp += delegate
            {
                _isUserDraggingSlider = false;
                if (_activeMediaElement != null && _activeMediaElement.NaturalDuration.HasTimeSpan)
                {
                    double totalSec = _activeMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    _activeMediaElement.Position = TimeSpan.FromSeconds((_mediaSeekSlider.Value / 100.0) * totalSec);
                }
            };
            Grid.SetColumn(_mediaSeekSlider, 1);
            barGrid.Children.Add(_mediaSeekSlider);

            _mediaTimeText = new TextBlock
            {
                Text = "00:00 / 00:00",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetSecondaryTextBrush(),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            Grid.SetColumn(_mediaTimeText, 2);
            barGrid.Children.Add(_mediaTimeText);

            barBorder.Child = barGrid;
            Grid.SetRow(barBorder, 1);
            mainGrid.Children.Add(barBorder);

            // Hook media events
            _activeMediaElement.MediaOpened += delegate
            {
                if (_activeMediaElement.NaturalDuration.HasTimeSpan)
                {
                    TimeSpan dur = _activeMediaElement.NaturalDuration.TimeSpan;
                    _mediaTimeText.Text = string.Format("00:00 / {0:mm\\:ss}", dur);
                }
            };
            _activeMediaElement.MediaEnded += delegate
            {
                _isMediaPlaying = false;
                _mediaPlayPauseBtn.Content = "▶";
                _mediaSeekSlider.Value = 0;
                if (_activeVinylTransform != null)
                {
                    _activeVinylTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                }
            };

            // Start Playback
            _activeMediaElement.Play();
            _isMediaPlaying = true;
            _mediaPlayPauseBtn.Content = "⏸";

            // Progress timer
            _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _mediaTimer.Tick += delegate
            {
                if (_activeMediaElement != null && _activeMediaElement.NaturalDuration.HasTimeSpan && !_isUserDraggingSlider)
                {
                    TimeSpan cur = _activeMediaElement.Position;
                    TimeSpan tot = _activeMediaElement.NaturalDuration.TimeSpan;
                    if (tot.TotalSeconds > 0)
                    {
                        _mediaSeekSlider.Value = (cur.TotalSeconds / tot.TotalSeconds) * 100.0;
                        _mediaTimeText.Text = string.Format("{0:mm\\:ss} / {1:mm\\:ss}", cur, tot);
                    }
                }
            };
            _mediaTimer.Start();

            return mainGrid;
        }

        private void ToggleMediaPlayPause()
        {
            if (_activeMediaElement == null) return;

            if (_isMediaPlaying)
            {
                _activeMediaElement.Pause();
                _isMediaPlaying = false;
                if (_mediaPlayPauseBtn != null) _mediaPlayPauseBtn.Content = "▶";
                if (_activeVinylTransform != null)
                {
                    _activeVinylTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                }
            }
            else
            {
                _activeMediaElement.Play();
                _isMediaPlaying = true;
                if (_mediaPlayPauseBtn != null) _mediaPlayPauseBtn.Content = "⏸";
                if (_activeVinylTransform != null)
                {
                    var spinAnim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3.5)))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    _activeVinylTransform.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
                }
            }
        }

        private void CleanupActiveMedia()
        {
            try
            {
                if (_activeVinylTransform != null)
                {
                    _activeVinylTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                    _activeVinylTransform = null;
                }
                if (_mediaTimer != null)
                {
                    _mediaTimer.Stop();
                    _mediaTimer = null;
                }
                if (_activeMediaElement != null)
                {
                    _activeMediaElement.Stop();
                    _activeMediaElement.Close();
                    _activeMediaElement = null;
                }
                _isMediaPlaying = false;
            }
            catch { }
        }

        // 4. DOCUMENT PRESENTER (Shell Thumbnail Factory + OpenXML parser)
        private UIElement CreateDocumentViewer(string path, string ext, ref string footerDetails)
        {
            // For .docx: extract text from word/document.xml directly!
            if (ext == ".docx")
            {
                string docText = ExtractDocxText(path);
                if (!string.IsNullOrEmpty(docText))
                {
                    return CreateScrollableTextViewer(docText, "Word Document Preview (.docx)", ref footerDetails);
                }
            }

            // Default: Render crisp Shell Thumbnail at 1024x1024
            BitmapSource thumb = GetShellThumbnail(path, 1024, 1024);
            if (thumb != null)
            {
                var grid = new Grid();
                var img = new Image
                {
                    Source = thumb,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
                return grid;
            }

            return CreateFallbackViewer(path, ref footerDetails);
        }

        private string ExtractDocxText(string docxPath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(docxPath))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry != null)
                    {
                        using (var stream = entry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string xml = reader.ReadToEnd();
                            // Simple, robust XML text extraction
                            var sb = new StringBuilder();
                            int i = 0;
                            while (i < xml.Length)
                            {
                                int tagStart = xml.IndexOf("<w:t", i);
                                if (tagStart < 0) break;
                                int valStart = xml.IndexOf('>', tagStart);
                                if (valStart < 0) break;
                                int valEnd = xml.IndexOf("</w:t>", valStart);
                                if (valEnd < 0) break;

                                string text = xml.Substring(valStart + 1, valEnd - valStart - 1);
                                sb.Append(text);

                                int pEnd = xml.IndexOf("</w:p>", valEnd);
                                int nextT = xml.IndexOf("<w:t", valEnd);
                                if (pEnd > 0 && (nextT < 0 || pEnd < nextT))
                                {
                                    sb.AppendLine().AppendLine();
                                }

                                i = valEnd + 6;
                            }
                            return sb.ToString();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // 5. ARCHIVE PRESENTER (Zip / Package Inspector)
        private UIElement CreateArchiveViewer(string path, ref string footerDetails)
        {
            var scroll = new ScrollViewer { Padding = new Thickness(14) };
            var stack = new StackPanel();

            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    int totalFiles = zip.Entries.Count;
                    long totalCompressed = 0;
                    long totalUncompressed = 0;

                    var headerBlock = new TextBlock
                    {
                        Text = string.Format("Archive contains {0} entries:", totalFiles),
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = GetPrimaryTextBrush(),
                        Margin = new Thickness(0, 0, 0, 10),
                        FontFamily = new FontFamily("Segoe UI")
                    };
                    stack.Children.Add(headerBlock);

                    int count = 0;
                    foreach (var entry in zip.Entries)
                    {
                        if (count++ > 200)
                        {
                            stack.Children.Add(new TextBlock
                            {
                                Text = string.Format("... and {0} more entries.", totalFiles - 200),
                                FontStyle = FontStyles.Italic,
                                Foreground = GetSecondaryTextBrush(),
                                Margin = new Thickness(4)
                            });
                            break;
                        }

                        totalCompressed += entry.CompressedLength;
                        totalUncompressed += entry.Length;

                        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var nameBlock = new TextBlock
                        {
                            Text = entry.FullName,
                            FontSize = 11,
                            Foreground = GetPrimaryTextBrush(),
                            FontFamily = new FontFamily("Segoe UI")
                        };
                        Grid.SetColumn(nameBlock, 0);
                        row.Children.Add(nameBlock);

                        var sizeBlock = new TextBlock
                        {
                            Text = string.Format("{0} ({1:P0})", FormatBytes(entry.Length), entry.Length > 0 ? (1.0 - (double)entry.CompressedLength / entry.Length) : 0),
                            FontSize = 10.5,
                            Foreground = GetAccentSubTextBrush(),
                            FontFamily = new FontFamily("Segoe UI")
                        };
                        Grid.SetColumn(sizeBlock, 1);
                        row.Children.Add(sizeBlock);

                        stack.Children.Add(row);
                    }

                    footerDetails = string.Format("{0} entries  •  {1} uncompressed  •  {2}", totalFiles, FormatBytes(totalUncompressed), footerDetails);
                }
            }
            catch (Exception ex)
            {
                return CreateErrorViewer("Could not inspect archive: " + ex.Message);
            }

            scroll.Content = stack;
            return scroll;
        }

        // 6. FOLDER PRESENTER
        private UIElement CreateFolderViewer(string dirPath, out string footerDetails)
        {
            footerDetails = "Folder";
            var scroll = new ScrollViewer { Padding = new Thickness(20) };
            var stack = new StackPanel();

            int fileCount = 0;
            int dirCount = 0;
            long totalBytes = 0;

            try
            {
                var dirInfo = new DirectoryInfo(dirPath);
                var subDirs = dirInfo.GetDirectories();
                var files = dirInfo.GetFiles();
                dirCount = subDirs.Length;
                fileCount = files.Length;

                foreach (var f in files) totalBytes += f.Length;

                var title = new TextBlock
                {
                    Text = "Folder: " + dirInfo.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = GetPrimaryTextBrush(),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                stack.Children.Add(title);

                var subtitle = new TextBlock
                {
                    Text = string.Format("{0} files, {1} subfolders • Total: {2}", fileCount, dirCount, FormatBytes(totalBytes)),
                    FontSize = 11.5,
                    Foreground = GetAccentSubTextBrush(),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                stack.Children.Add(subtitle);

                // Top Files List
                int displayLimit = Math.Min(files.Length, 35);
                for (int i = 0; i < displayLimit; i++)
                {
                    var f = files[i];
                    var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var fn = new TextBlock { Text = f.Name, FontSize = 11, Foreground = GetPrimaryTextBrush() };
                    var fs = new TextBlock { Text = FormatBytes(f.Length), FontSize = 10.5, Foreground = GetSecondaryTextBrush() };

                    Grid.SetColumn(fn, 0);
                    Grid.SetColumn(fs, 1);
                    row.Children.Add(fn);
                    row.Children.Add(fs);
                    stack.Children.Add(row);
                }

                if (files.Length > 35)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = string.Format("... and {0} more files", files.Length - 35),
                        FontStyle = FontStyles.Italic,
                        Foreground = GetSecondaryTextBrush(),
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                }
            }
            catch (Exception ex)
            {
                return CreateErrorViewer("Could not read directory: " + ex.Message);
            }

            footerDetails = string.Format("{0} files  •  {1} folders  •  {2}", fileCount, dirCount, FormatBytes(totalBytes));
            scroll.Content = stack;
            return scroll;
        }

        // 7. FALLBACK / BINARY HEX DUMP PRESENTER
        private UIElement CreateFallbackViewer(string path, ref string footerDetails)
        {
            // First attempt: High-res Shell Thumbnail
            BitmapSource thumb = GetShellThumbnail(path, 512, 512);
            if (thumb != null)
            {
                var grid = new Grid();
                var img = new Image
                {
                    Source = thumb,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
                return grid;
            }

            // Hex Dump View (First 2 KB)
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var banner = new Border
            {
                Background = GetChamberBgBrush(),
                Padding = new Thickness(14, 6, 14, 6),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = GetChamberBorderBrush()
            };
            banner.Child = new TextBlock
            {
                Text = "Binary Hex View (First 2048 bytes)",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = GetSecondaryTextBrush()
            };
            Grid.SetRow(banner, 0);
            root.Children.Add(banner);

            var scroll = new ScrollViewer { Padding = new Thickness(12) };
            var hexBlock = new TextBlock
            {
                FontSize = 11,
                FontFamily = new FontFamily("Consolas, Courier New"),
                Foreground = GetPrimaryTextBrush(),
                LineHeight = 17
            };

            try
            {
                byte[] buffer = new byte[2048];
                int bytesRead = 0;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytesRead = fs.Read(buffer, 0, buffer.Length);
                }

                var sb = new StringBuilder();
                for (int i = 0; i < bytesRead; i += 16)
                {
                    sb.AppendFormat("{0:X8}  ", i);
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < bytesRead) sb.AppendFormat("{0:X2} ", buffer[i + j]);
                        else sb.Append("   ");
                        if (j == 7) sb.Append(" ");
                    }
                    sb.Append(" |");
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < bytesRead)
                        {
                            byte b = buffer[i + j];
                            sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
                        }
                    }
                    sb.AppendLine("|");
                }
                hexBlock.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                hexBlock.Text = "Could not generate hex view: " + ex.Message;
            }

            scroll.Content = hexBlock;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            return root;
        }

        private UIElement CreateScrollableTextViewer(string text, string title, ref string footerDetails)
        {
            var scroll = new ScrollViewer { Padding = new Thickness(16) };
            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(titleBlock);

            var bodyBlock = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = GetPrimaryTextBrush(),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            };
            stack.Children.Add(bodyBlock);

            scroll.Content = stack;
            return scroll;
        }

        private UIElement CreateErrorViewer(string message)
        {
            var grid = new Grid();
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "⚠",
                FontSize = 36,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });
            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x48, 0x3E)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
                TextAlignment = TextAlignment.Center
            });
            grid.Children.Add(stack);
            return grid;
        }

        private BitmapSource GetShellThumbnail(string path, int width, int height)
        {
            try
            {
                Guid uuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                IShellItemImageFactory factory;
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, uuid, out factory);
                if (hr == 0 && factory != null)
                {
                    try
                    {
                        IntPtr hbmp;
                        int imgHr = factory.GetImage(new SIZE(width, height), 0, out hbmp);
                        if (imgHr == 0 && hbmp != IntPtr.Zero)
                        {
                            try
                            {
                                BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    hbmp,
                                    IntPtr.Zero,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                bs.Freeze();
                                return bs;
                            }
                            finally
                            {
                                DeleteObject(hbmp);
                            }
                        }
                    }
                    finally
                    {
                        try { Marshal.ReleaseComObject(factory); } catch { }
                    }
                }
            }
            catch { }
            return null;
        }
        #endregion

        #region Extension Categorization
        private static readonly HashSet<string> _imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif", ".svg"
        };
        private static readonly HashSet<string> _videoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v"
        };
        private static readonly HashSet<string> _audioExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma"
        };
        private static readonly HashSet<string> _codeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".ts", ".json", ".xml", ".html", ".css", ".py", ".c", ".cpp", ".h", ".hpp",
            ".java", ".rs", ".go", ".sql", ".sh", ".bat", ".cmd", ".ps1", ".vbs", ".php", ".rb",
            ".swift", ".kt", ".dart", ".txt", ".log", ".ini", ".cfg", ".config", ".yml", ".yaml",
            ".toml", ".csv", ".tsv", ".reg", ".env", ".md", ".markdown"
        };
        private static readonly HashSet<string> _docExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt", ".odt", ".ods", ".odp"
        };
        private static readonly HashSet<string> _archiveExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".jar", ".war", ".nupkg"
        };

        private static bool IsImageExtension(string ext) { return _imageExts.Contains(ext); }
        private static bool IsVideoExtension(string ext) { return _videoExts.Contains(ext); }
        private static bool IsAudioExtension(string ext) { return _audioExts.Contains(ext); }
        private static bool IsCodeOrTextExtension(string ext) { return _codeExts.Contains(ext); }
        private static bool IsDocumentExtension(string ext) { return _docExts.Contains(ext); }
        private static bool IsArchiveExtension(string ext) { return _archiveExts.Contains(ext); }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }
        #endregion

        #region Actions
        private void OpenInDefaultApp()
        {
            if (_playlist == null || _playlist.Count == 0 || _currentIndex < 0 || _currentIndex >= _playlist.Count)
                return;

            string path = _playlist[_currentIndex];
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        private void ShowInContainingFolder()
        {
            if (_playlist == null || _playlist.Count == 0 || _currentIndex < 0 || _currentIndex >= _playlist.Count)
                return;

            string path = _playlist[_currentIndex];
            try
            {
                string args = string.Format("/select,\"{0}\"", path);
                Process.Start("explorer.exe", args);
            }
            catch { }
        }

        public void ApplyTheme(string accent, string mode)
        {
            _isDarkMode = (mode == "Dark");
            if (_isDarkMode)
            {
                _rootBorder.Background = new LinearGradientBrush(
                    Color.FromRgb(0x1E, 0x1A, 0x29),
                    Color.FromRgb(0x12, 0x0F, 0x1A),
                    new Point(0, 0), new Point(0, 1));
                _rootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0x38, 0x54));

                _headerBorder.Background = new LinearGradientBrush(
                    Color.FromRgb(0x26, 0x20, 0x33),
                    Color.FromRgb(0x1A, 0x16, 0x24),
                    new Point(0, 0), new Point(0, 1));
                _headerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x34, 0x50));

                _contentHost.Background = new LinearGradientBrush(
                    Color.FromRgb(0x14, 0x11, 0x1E),
                    Color.FromRgb(0x0E, 0x0B, 0x15),
                    new Point(0, 0), new Point(0, 1));
                _contentHost.BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x2A, 0x42));

                _fileNameBlock.Foreground = Brushes.White;
                _counterBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                _footerLeftText.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                _footerRightText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));

                StyleButtonDark(_btnPrev);
                StyleButtonDark(_btnNext);
                StyleButtonDark(_btnOpen);
                StyleButtonDark(_btnFolder);
                StyleButtonDark(_btnClose);
            }
            else
            {
                _rootBorder.Background = new LinearGradientBrush(
                    Color.FromRgb(0xF6, 0xF3, 0xED),
                    Color.FromRgb(0xEA, 0xE4, 0xD6),
                    new Point(0, 0), new Point(0, 1));
                _rootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xBD, 0xAC));

                _headerBorder.Background = new LinearGradientBrush(
                    Color.FromRgb(0xFC, 0xFA, 0xF6),
                    Color.FromRgb(0xEE, 0xE8, 0xDC),
                    new Point(0, 0), new Point(0, 1));
                _headerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD2, 0xCB, 0xBC));

                _contentHost.Background = new LinearGradientBrush(
                    Color.FromRgb(0xEB, 0xE6, 0xDC),
                    Color.FromRgb(0xF5, 0xF2, 0xEB),
                    new Point(0, 0), new Point(0, 1));
                _contentHost.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD0, 0xC0));

                _fileNameBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x1C, 0x2A));
                _counterBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x58, 0x4A));
                _footerLeftText.Foreground = new SolidColorBrush(Color.FromRgb(0x72, 0x6A, 0x5C));
                _footerRightText.Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C));

                StyleButtonLight(_btnPrev);
                StyleButtonLight(_btnNext);
                StyleButtonLight(_btnOpen);
                StyleButtonLight(_btnFolder);
                StyleButtonLight(_btnClose);
            }

            if (IsVisible)
            {
                LoadCurrentFile();
            }
        }

        private void StyleButtonDark(Button btn)
        {
            if (btn == null) return;
            btn.Background = new LinearGradientBrush(
                Color.FromRgb(0x2C, 0x26, 0x3B),
                Color.FromRgb(0x1F, 0x1B, 0x2B),
                new Point(0, 0), new Point(0, 1));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x48, 0x3E, 0x5E));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
        }

        private void StyleButtonLight(Button btn)
        {
            if (btn == null) return;
            btn.Background = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0xFF, 0xFF),
                Color.FromRgb(0xEB, 0xE5, 0xD8),
                new Point(0, 0), new Point(0, 1));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xC8, 0xB8));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0x35, 0x2E, 0x40));
        }
        #endregion
    }
}