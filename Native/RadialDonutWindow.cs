using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalConvertNative.Engine;
using LocalConvertNative.Shell;

namespace LocalConvertNative.UI
{
    public class DonutSubWedge
    {
        public string Id { get; set; }
        public string PrimaryLabel { get; set; }
        public string TargetAction { get; set; }
    }

    public class DonutWedge
    {
        public string Id { get; set; }
        public string PrimaryLabel { get; set; }
        public string SubLabel { get; set; }
        public string TargetAction { get; set; }
        public List<DonutSubWedge> SubWedges { get; set; }

        public DonutWedge()
        {
            SubWedges = new List<DonutSubWedge>();
        }
    }

    public class RadialDonutWindow : Window
    {
        private List<string> _selectedFiles = new List<string>();
        private bool _isBackFace = false;
        private int _hoveredWedgeIndex = -1;
        private int _activeSubGroupWedgeIndex = -1;
        private int _hoveredSubWedgeIndex = -1;
        private double _subArcAnimProgress = 0.0;
        private DispatcherTimer _animTimer;
        private ProgressFeedbackWindow _progressOverlay;

        private ScaleTransform _scaleTransform;
        private ScaleTransform _flipTransform;
        private TransformGroup _transformGroup;
        private double _baseScale = 1.0;
        private bool _isClosing = false;
        private bool _isFlipping = false;

        private Point _currentMouse = new Point(-1, -1);
        private ConversionOptions _options = new ConversionOptions();

        private List<DonutWedge> _frontWedges = new List<DonutWedge>();
        private List<DonutWedge> _backWedges = new List<DonutWedge>();

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
            return GetWindowLong32(hWnd, nIndex);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        public bool IsSingleShot { get; set; }
        public string DefaultWheelLayer { get; set; }
        public bool PlaySoundOnComplete { get; set; }
        public bool ShowNotifications { get; set; }
        public event Action<string> OnStatusMessage;
        public event Action OnDonutClosed;

        public ConversionOptions Options
        {
            get { return _options; }
            set { if (value != null) _options = value; }
        }

        public RadialDonutWindow()
        {
            DefaultWheelLayer = "Primary";
            PlaySoundOnComplete = true;
            ShowNotifications = true;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            Width = 660;
            Height = 660;

            _scaleTransform = new ScaleTransform(1.0, 1.0, 330, 330);
            _flipTransform = new ScaleTransform(1.0, 1.0, 330, 330);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_flipTransform);
            _transformGroup.Children.Add(_scaleTransform);
            RenderTransform = _transformGroup;

            MouseMove += OnWindowMouseMove;
            MouseLeave += (s, e) =>
            {
                _currentMouse = new Point(-1, -1);
                _hoveredWedgeIndex = -1;
                _hoveredSubWedgeIndex = -1;
                StartSubArcAnim(false);
                InvalidateVisual();
            };
            MouseDown += OnWindowMouseDown;
            MouseUp += OnWindowMouseUp;
            MouseWheel += OnWindowMouseWheel;
            KeyDown += OnWindowKeyDown;

            // 150ms spring animation timer for sub-arcs
            _animTimer = new DispatcherTimer(DispatcherPriority.Render);
            _animTimer.Interval = TimeSpan.FromMilliseconds(16);
            _animTimer.Tick += OnAnimTick;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                long exStyle = GetWindowLongPtr(helper.Handle, GWL_EXSTYLE).ToInt64();
                long newStyle = exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                SetWindowLongPtr(helper.Handle, GWL_EXSTYLE, new IntPtr(newStyle));

                bool isDwm = true;
                try { DwmIsCompositionEnabled(out isDwm); } catch { }

                // Win11: Disable DWM rounded corner clipping on circular wheel
                int cornerPreference = 1; // DWMWCP_DONOTROUND
                DwmSetWindowAttribute(helper.Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        private bool _animExpanding = false;
        private void StartSubArcAnim(bool expand)
        {
            _animExpanding = expand;
            if (!_animTimer.IsEnabled)
            {
                _animTimer.Start();
            }
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (_animExpanding)
            {
                // Spring cubic ease-out towards 1.0 (150ms duration)
                double diff = 1.0 - _subArcAnimProgress;
                if (diff <= 0.02)
                {
                    _subArcAnimProgress = 1.0;
                    _animTimer.Stop();
                }
                else
                {
                    _subArcAnimProgress += Math.Max(0.04, diff * 0.35);
                }
            }
            else
            {
                // Ease towards 0.0
                if (_subArcAnimProgress <= 0.04)
                {
                    _subArcAnimProgress = 0.0;
                    _activeSubGroupWedgeIndex = -1;
                    _hoveredSubWedgeIndex = -1;
                    _animTimer.Stop();
                }
                else
                {
                    _subArcAnimProgress -= Math.Max(0.04, _subArcAnimProgress * 0.35);
                }
            }
            InvalidateVisual();
        }

        public void ShowAt(int screenX, int screenY, List<string> selectedFiles)
        {
            if (selectedFiles != null && selectedFiles.Count > 0)
            {
                _selectedFiles = selectedFiles;
            }
            else
            {
                _selectedFiles = ExplorerSelectionHelper.GetSelectedFilePaths();
            }

            BuildWedgesForFiles();

            _isBackFace = (DefaultWheelLayer == "Secondary");
            _hoveredWedgeIndex = -1;
            _activeSubGroupWedgeIndex = -1;
            _hoveredSubWedgeIndex = -1;
            _subArcAnimProgress = 0.0;
            if (_animTimer.IsEnabled) _animTimer.Stop();

            var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(screenX, screenY));
            var work = scr != null ? scr.WorkingArea : new System.Drawing.Rectangle(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);

            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.EnsureHandle();
                var source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            }
            catch { }
            if (dpiScaleX <= 0) dpiScaleX = 1.0;
            if (dpiScaleY <= 0) dpiScaleY = 1.0;

            double dipX = screenX / dpiScaleX;
            double dipY = screenY / dpiScaleY;

            double minX = (work.Left / dpiScaleX) + 10;
            double maxX = (work.Right / dpiScaleX) - Width - 10;
            double minY = (work.Top / dpiScaleY) + 10;
            double maxY = (work.Bottom / dpiScaleY) - Height - 10;

            double x = dipX - (Width / 2.0);
            double y = dipY - (Height / 2.0);

            x = Math.Max(minX, Math.Min(maxX, x));
            y = Math.Max(minY, Math.Min(maxY, y));

            Left = x;
            Top = y;

            _isClosing = false;
            _isFlipping = false;

            // Reset any running transform animations
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _flipTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            BeginAnimation(OpacityProperty, null);

            _scaleTransform.CenterX = Width / 2.0;
            _scaleTransform.CenterY = Height / 2.0;
            _flipTransform.CenterX = Width / 2.0;
            _flipTransform.CenterY = Height / 2.0;
            _flipTransform.ScaleX = 1.0;
            _flipTransform.ScaleY = 1.0;

            Opacity = 0.0;
            Show();

            // Fluid pop-in entrance: 0.84 -> 1.0 with BackEaseOut (140ms), Opacity 0 -> 1 (120ms)
            var popAnimX = new DoubleAnimation(_baseScale * 0.84, _baseScale, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                EasingFunction = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }
            };
            var popAnimY = new DoubleAnimation(_baseScale * 0.84, _baseScale, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                EasingFunction = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut }
            };
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, popAnimX);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, popAnimY);

            var fadeAnim = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(120)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeAnim);
            InvalidateVisual();
        }

        public void FlipFace()
        {
            if (_isFlipping) return;
            _isFlipping = true;

            _hoveredWedgeIndex = -1;
            _activeSubGroupWedgeIndex = -1;
            _hoveredSubWedgeIndex = -1;
            _subArcAnimProgress = 0.0;
            if (_animTimer.IsEnabled) _animTimer.Stop();

            // Squeeze horizontally (ScaleX 1.0 -> 0.02 in 85ms)
            var flipIn = new DoubleAnimation(1.0, 0.02, new Duration(TimeSpan.FromMilliseconds(85)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            flipIn.Completed += (s, e) =>
            {
                _isBackFace = !_isBackFace;

                // Immediately re-test current cursor position on newly active wheel
                try
                {
                    KeyboardHook.POINT pt;
                    if (KeyboardHook.GetCursorPos(out pt))
                    {
                        Point localPt = PointFromScreen(new Point(pt.X, pt.Y));
                        HitTestDonut(localPt);
                    }
                }
                catch { }

                InvalidateVisual();

                // Expand horizontally with subtle overshoot bounce (ScaleX 0.02 -> 1.0 in 125ms)
                var flipOut = new DoubleAnimation(0.02, 1.0, new Duration(TimeSpan.FromMilliseconds(125)))
                {
                    EasingFunction = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut }
                };
                flipOut.Completed += (s2, e2) =>
                {
                    _isFlipping = false;
                };
                _flipTransform.BeginAnimation(ScaleTransform.ScaleXProperty, flipOut);
            };

            _flipTransform.BeginAnimation(ScaleTransform.ScaleXProperty, flipIn);
        }

        public void ApplyTheme(string themeName, string themeMode = null)
        {
            CurdTheme.SetTheme(themeName, themeMode);
            InvalidateVisual();
        }

        public void ApplyScale(string scaleName)
        {
            if (scaleName == "Compact") _baseScale = 0.85;
            else if (scaleName == "Large") _baseScale = 1.15;
            else _baseScale = 1.0;

            if (_scaleTransform != null)
            {
                _scaleTransform.CenterX = Width / 2.0;
                _scaleTransform.CenterY = Height / 2.0;
                _scaleTransform.ScaleX = _baseScale;
                _scaleTransform.ScaleY = _baseScale;
            }
        }

        public void UpdateMousePosition(int screenX, int screenY)
        {
            if (!IsVisible) return;
            double dpiScaleX = 1.0, dpiScaleY = 1.0;
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.EnsureHandle();
                var source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            }
            catch { }
            if (dpiScaleX <= 0) dpiScaleX = 1.0;
            if (dpiScaleY <= 0) dpiScaleY = 1.0;

            Point dipPt = new Point(screenX / dpiScaleX, screenY / dpiScaleY);
            Point localPt = PointFromScreen(dipPt);
            _currentMouse = localPt;

            int prevWedge = _hoveredWedgeIndex;
            int prevSubWedge = _hoveredSubWedgeIndex;
            HitTestDonut(_currentMouse);
            if (_hoveredWedgeIndex != prevWedge || _hoveredSubWedgeIndex != prevSubWedge)
            {
                InvalidateVisual();
            }
        }

        public void CloseDonut(bool actionExecuting = false)
        {
            if (_isClosing) return;

            if (actionExecuting || !IsVisible)
            {
                _isClosing = false;
                Hide();
                _subArcAnimProgress = 0.0;
                if (_animTimer.IsEnabled) _animTimer.Stop();

                if (OnDonutClosed != null)
                {
                    OnDonutClosed();
                }

                if (!actionExecuting)
                {
                    Program.TrimMemory();
                }

                if (IsSingleShot && !actionExecuting)
                {
                    System.Windows.Application.Current.Shutdown();
                }
                return;
            }

            _isClosing = true;
            _subArcAnimProgress = 0.0;
            if (_animTimer.IsEnabled) _animTimer.Stop();

            // Smooth exit animation: shrink (1.0 -> 0.88) & fade (Opacity -> 0.0) in 75ms
            var exitScaleX = new DoubleAnimation(_baseScale, _baseScale * 0.88, new Duration(TimeSpan.FromMilliseconds(75)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var exitScaleY = new DoubleAnimation(_baseScale, _baseScale * 0.88, new Duration(TimeSpan.FromMilliseconds(75)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var exitFade = new DoubleAnimation(Opacity, 0.0, new Duration(TimeSpan.FromMilliseconds(75)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            exitFade.Completed += (s, e) =>
            {
                _isClosing = false;
                Hide();

                if (OnDonutClosed != null)
                {
                    OnDonutClosed();
                }

                Program.TrimMemory();

                if (IsSingleShot)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };

            _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, exitScaleX);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, exitScaleY);
            BeginAnimation(OpacityProperty, exitFade);
        }

        public void ExecuteHoveredOrClose()
        {
            if (!IsVisible) return;

            // Only fallback to cursor position check if nothing is hovered yet!
            if (_hoveredWedgeIndex < 0 && _hoveredSubWedgeIndex < 0)
            {
                try
                {
                    KeyboardHook.POINT pt;
                    if (KeyboardHook.GetCursorPos(out pt))
                    {
                        Point localPt = PointFromScreen(new Point(pt.X, pt.Y));
                        HitTestDonut(localPt);
                    }
                }
                catch { }
            }

            var activeWedges = _isBackFace ? _backWedges : _frontWedges;
            bool actionExecuted = false;

            // 1. If hovered on a sub-wedge (Crop, Rotate, Flip sub-options)
            if (_hoveredSubWedgeIndex >= 0 && _activeSubGroupWedgeIndex >= 0 && _activeSubGroupWedgeIndex < activeWedges.Count)
            {
                var parent = activeWedges[_activeSubGroupWedgeIndex];
                if (_hoveredSubWedgeIndex < parent.SubWedges.Count)
                {
                    var sub = parent.SubWedges[_hoveredSubWedgeIndex];
                    actionExecuted = true;
                    ExecuteTargetAction(sub.TargetAction, sub.PrimaryLabel);
                }
            }
            // 2. If hovered on primary wedge
            else if (_hoveredWedgeIndex >= 0 && _hoveredWedgeIndex < activeWedges.Count)
            {
                var wedge = activeWedges[_hoveredWedgeIndex];
                actionExecuted = true;
                ExecuteAction(wedge);
            }

            CloseDonut(actionExecuted);
        }

        private void BuildWedgesForFiles()
        {
            string ext = "jpg";
            if (_selectedFiles.Count > 0)
            {
                ext = Path.GetExtension(_selectedFiles[0]).TrimStart('.').ToLowerInvariant();
            }

            _frontWedges.Clear();
            _backWedges.Clear();

            // 1. Image Files (JPG, PNG, WEBP, BMP, GIF, TIFF, ICO, etc.)
            if (ext == "jpg" || ext == "jpeg" || ext == "png" || ext == "webp" ||
                ext == "bmp" || ext == "gif" || ext == "tiff" || ext == "tif" ||
                ext == "ico" || ext == "heic" || ext == "avif")
            {
                // Front Wheel (9 Wedges) - Dynamically provides JPG or PNG based on source
                if (ext == "png")
                {
                    _frontWedges.Add(new DonutWedge { Id = "jpg", PrimaryLabel = "JPG", SubLabel = "JPEG", TargetAction = "jpg" }); // 12:00
                    _frontWedges.Add(new DonutWedge { Id = "webp", PrimaryLabel = "WEBP", SubLabel = "WEBP", TargetAction = "webp" }); // ~1:30
                }
                else if (ext == "jpg" || ext == "jpeg")
                {
                    _frontWedges.Add(new DonutWedge { Id = "png", PrimaryLabel = "PNG", SubLabel = "PNG", TargetAction = "png" }); // 12:00
                    _frontWedges.Add(new DonutWedge { Id = "webp", PrimaryLabel = "WEBP", SubLabel = "WEBP", TargetAction = "webp" }); // ~1:30
                }
                else
                {
                    _frontWedges.Add(new DonutWedge { Id = "png", PrimaryLabel = "PNG", SubLabel = "PNG", TargetAction = "png" }); // 12:00
                    _frontWedges.Add(new DonutWedge { Id = "jpg", PrimaryLabel = "JPG", SubLabel = "JPEG", TargetAction = "jpg" }); // ~1:30
                }

                _frontWedges.Add(new DonutWedge { Id = "pdf", PrimaryLabel = "PDF", SubLabel = "PDF", TargetAction = "pdf" }); // ~3:00
                _frontWedges.Add(new DonutWedge { Id = "trace", PrimaryLabel = "Trace to SVG", SubLabel = "SVG", TargetAction = "trace-svg" }); // ~4:30
                _frontWedges.Add(new DonutWedge { Id = "lineart", PrimaryLabel = "Line art to SVG", SubLabel = "SVG", TargetAction = "lineart-svg" }); // ~6:00
                _frontWedges.Add(new DonutWedge { Id = "tiff", PrimaryLabel = (ext == "tiff" || ext == "tif") ? "WEBP" : "TIFF", SubLabel = (ext == "tiff" || ext == "tif") ? "WEBP" : "TIFF", TargetAction = (ext == "tiff" || ext == "tif") ? "webp" : "tiff" }); // ~7:00
                _frontWedges.Add(new DonutWedge { Id = "bmp", PrimaryLabel = (ext == "bmp") ? "WEBP" : "BMP", SubLabel = (ext == "bmp") ? "WEBP" : "BMP", TargetAction = (ext == "bmp") ? "webp" : "bmp" }); // ~8:30
                _frontWedges.Add(new DonutWedge { Id = "ico", PrimaryLabel = (ext == "ico") ? "WEBP" : "ICO", SubLabel = (ext == "ico") ? "WEBP" : "ICO", TargetAction = (ext == "ico") ? "webp" : "ico" }); // ~9:30
                _frontWedges.Add(new DonutWedge { Id = "half", PrimaryLabel = "Half size", SubLabel = ext.ToUpperInvariant(), TargetAction = "half-size" }); // ~11:00

                // Back Wheel (10 Wedges with Expandable Sub-Arcs for Crop, Rotate, Flip)
                // Wedge 0: Crop (Auto, 1:1, 16:9)
                var cropWedge = new DonutWedge { Id = "crop", PrimaryLabel = "Crop", SubLabel = "Group of 3", TargetAction = "crop-auto" };
                cropWedge.SubWedges.Add(new DonutSubWedge { Id = "crop-auto", PrimaryLabel = "Auto-Crop Border", TargetAction = "crop-auto" });
                cropWedge.SubWedges.Add(new DonutSubWedge { Id = "crop-1-1", PrimaryLabel = "Square Crop (1:1)", TargetAction = "crop-1-1" });
                cropWedge.SubWedges.Add(new DonutSubWedge { Id = "crop-16-9", PrimaryLabel = "16:9 Aspect Crop", TargetAction = "crop-16-9" });
                _backWedges.Add(cropWedge); // 12:00

                // Wedge 1: Rotate (90 CW, 90 CCW, 180)
                var rotWedge = new DonutWedge { Id = "rotate", PrimaryLabel = "Rotate", SubLabel = "Group of 3", TargetAction = "rotate-90-cw" };
                rotWedge.SubWedges.Add(new DonutSubWedge { Id = "rot-cw", PrimaryLabel = "90° Clockwise", TargetAction = "rotate-90-cw" });
                rotWedge.SubWedges.Add(new DonutSubWedge { Id = "rot-ccw", PrimaryLabel = "90° Counter-CW", TargetAction = "rotate-90-ccw" });
                rotWedge.SubWedges.Add(new DonutSubWedge { Id = "rot-180", PrimaryLabel = "180° Rotate", TargetAction = "rotate-180" });
                _backWedges.Add(rotWedge); // ~1:15

                // Wedge 2: Flip (Flip H, Flip V)
                var flipWedge = new DonutWedge { Id = "flip", PrimaryLabel = "Flip", SubLabel = "Group of 2", TargetAction = "flip-h" };
                flipWedge.SubWedges.Add(new DonutSubWedge { Id = "flip-h", PrimaryLabel = "Flip Horizontal", TargetAction = "flip-h" });
                flipWedge.SubWedges.Add(new DonutSubWedge { Id = "flip-v", PrimaryLabel = "Flip Vertical", TargetAction = "flip-v" });
                _backWedges.Add(flipWedge); // ~2:30

                _backWedges.Add(new DonutWedge { Id = "rembg", PrimaryLabel = "Remove background", SubLabel = "PNG", TargetAction = "remove-bg" }); // ~4:00
                _backWedges.Add(new DonutWedge { Id = "gray", PrimaryLabel = "Grayscale", SubLabel = ext.ToUpperInvariant(), TargetAction = "grayscale" }); // ~5:15
                _backWedges.Add(new DonutWedge { Id = "live", PrimaryLabel = "Extract Live Photo", SubLabel = "MP4", TargetAction = "live-photo" }); // ~6:30
                _backWedges.Add(new DonutWedge { Id = "avif", PrimaryLabel = "AVIF", SubLabel = "AVIF", TargetAction = "avif" }); // ~7:45
                _backWedges.Add(new DonutWedge { Id = "gif", PrimaryLabel = "GIF", SubLabel = "GIF", TargetAction = "gif" }); // ~9:00

                // OCR group (Extract to Text or Word)
                var imgOcrWedge = new DonutWedge { Id = "ocr", PrimaryLabel = "OCR", SubLabel = "Group of 2", TargetAction = "ocr-txt" };
                imgOcrWedge.SubWedges.Add(new DonutSubWedge { Id = "ocr-txt", PrimaryLabel = "OCR to Text", TargetAction = "ocr-txt" });
                imgOcrWedge.SubWedges.Add(new DonutSubWedge { Id = "ocr-docx", PrimaryLabel = "OCR to Word", TargetAction = "ocr-docx" });
                _backWedges.Add(imgOcrWedge); // ~10:00

                // Secondary format option (Opposite primary format)
                if (ext == "jpg" || ext == "jpeg")
                {
                    _backWedges.Add(new DonutWedge { Id = "png_alt", PrimaryLabel = "PNG", SubLabel = "Lossless", TargetAction = "png" }); // ~10:30
                }
                else
                {
                    _backWedges.Add(new DonutWedge { Id = "jpg_alt", PrimaryLabel = "JPG", SubLabel = "JPEG", TargetAction = "jpg" }); // ~10:30
                }

                _backWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" }); // ~11:00
            }
            // 2. Documents (PDF, DOCX, DOC, TXT, MD, HTML, RTF)
            else if (ext == "pdf" || ext == "docx" || ext == "doc" || ext == "txt" || ext == "md" || ext == "html" || ext == "rtf")
            {
                _frontWedges.Add(new DonutWedge { Id = "pdf", PrimaryLabel = "PDF", SubLabel = "PDF", TargetAction = "pdf" });
                _frontWedges.Add(new DonutWedge { Id = "docx", PrimaryLabel = "DOCX", SubLabel = "DOCX", TargetAction = "docx" });
                _frontWedges.Add(new DonutWedge { Id = "doc", PrimaryLabel = "DOC", SubLabel = "DOC", TargetAction = "doc" });
                _frontWedges.Add(new DonutWedge { Id = "txt", PrimaryLabel = "TXT", SubLabel = "TXT", TargetAction = "txt" });
                _frontWedges.Add(new DonutWedge { Id = "html", PrimaryLabel = "HTML", SubLabel = "HTML", TargetAction = "html" });
                _frontWedges.Add(new DonutWedge { Id = "md", PrimaryLabel = "Markdown", SubLabel = "MD", TargetAction = "md" });
                _frontWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });
                _frontWedges.Add(new DonutWedge { Id = "png", PrimaryLabel = "PNG Image", SubLabel = "PNG", TargetAction = "png" });

                _backWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });
                _backWedges.Add(new DonutWedge { Id = "mergepdf", PrimaryLabel = "Merge PDF", SubLabel = "PDF", TargetAction = "merge-pdf" });
                _backWedges.Add(new DonutWedge { Id = "txt", PrimaryLabel = "Extract text", SubLabel = "TXT", TargetAction = "txt" });
                _backWedges.Add(new DonutWedge { Id = "html", PrimaryLabel = "HTML Webpage", SubLabel = "HTML", TargetAction = "html" });
                _backWedges.Add(new DonutWedge { Id = "docx", PrimaryLabel = "Word DOCX", SubLabel = "DOCX", TargetAction = "docx" });
                _backWedges.Add(new DonutWedge { Id = "pdf", PrimaryLabel = "Export PDF", SubLabel = "PDF", TargetAction = "pdf" });

                // OCR group for Documents (Scanned PDFs to Word / Text)
                var docOcrWedge = new DonutWedge { Id = "ocr", PrimaryLabel = "OCR", SubLabel = "Group of 2", TargetAction = "ocr-docx" };
                docOcrWedge.SubWedges.Add(new DonutSubWedge { Id = "ocr-docx", PrimaryLabel = "OCR to Word", TargetAction = "ocr-docx" });
                docOcrWedge.SubWedges.Add(new DonutSubWedge { Id = "ocr-txt", PrimaryLabel = "OCR to Text", TargetAction = "ocr-txt" });
                _backWedges.Add(docOcrWedge);
            }
            // 3. Spreadsheets & Data (XLSX, XLS, CSV, TSV, JSON, XML, YAML)
            else if (ext == "xlsx" || ext == "xls" || ext == "csv" || ext == "tsv" || ext == "json" || ext == "jsonl" || ext == "xml" || ext == "yaml" || ext == "yml")
            {
                _frontWedges.Add(new DonutWedge { Id = "csv", PrimaryLabel = "CSV", SubLabel = "CSV", TargetAction = "csv" });
                _frontWedges.Add(new DonutWedge { Id = "tsv", PrimaryLabel = "TSV", SubLabel = "TSV", TargetAction = "tsv" });
                _frontWedges.Add(new DonutWedge { Id = "json", PrimaryLabel = "JSON", SubLabel = "JSON", TargetAction = "json" });
                _frontWedges.Add(new DonutWedge { Id = "jsonl", PrimaryLabel = "JSONL", SubLabel = "JSONL", TargetAction = "jsonl" });
                _frontWedges.Add(new DonutWedge { Id = "html", PrimaryLabel = "HTML Table", SubLabel = "HTML", TargetAction = "html" });
                _frontWedges.Add(new DonutWedge { Id = "xml", PrimaryLabel = "XML", SubLabel = "XML", TargetAction = "xml" });
                _frontWedges.Add(new DonutWedge { Id = "xlsx", PrimaryLabel = "XLSX", SubLabel = "Excel", TargetAction = "xlsx" });
                _frontWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });

                _backWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });
                _backWedges.Add(new DonutWedge { Id = "md", PrimaryLabel = "Markdown Table", SubLabel = "MD", TargetAction = "md" });
                _backWedges.Add(new DonutWedge { Id = "yaml", PrimaryLabel = "YAML Document", SubLabel = "YAML", TargetAction = "yaml" });
                _backWedges.Add(new DonutWedge { Id = "csv", PrimaryLabel = "Delimited CSV", SubLabel = "CSV", TargetAction = "csv" });
                _backWedges.Add(new DonutWedge { Id = "json", PrimaryLabel = "JSON Array", SubLabel = "JSON", TargetAction = "json" });
                _backWedges.Add(new DonutWedge { Id = "html", PrimaryLabel = "HTML Webpage", SubLabel = "HTML", TargetAction = "html" });
                _backWedges.Add(new DonutWedge { Id = "tsv", PrimaryLabel = "TSV File", SubLabel = "TSV", TargetAction = "tsv" });
            }
            // 4. Subtitles & Transcripts (SRT, VTT, ASS, SSA)
            else if (ext == "srt" || ext == "vtt" || ext == "ass" || ext == "ssa")
            {
                _frontWedges.Add(new DonutWedge { Id = "srt", PrimaryLabel = "SRT Subtitle", SubLabel = "SRT", TargetAction = "srt" });
                _frontWedges.Add(new DonutWedge { Id = "vtt", PrimaryLabel = "VTT Subtitle", SubLabel = "VTT", TargetAction = "vtt" });
                _frontWedges.Add(new DonutWedge { Id = "txt", PrimaryLabel = "Clean Transcript", SubLabel = "TXT", TargetAction = "txt" });
                _frontWedges.Add(new DonutWedge { Id = "ass", PrimaryLabel = "ASS Advanced", SubLabel = "ASS", TargetAction = "ass" });
                _frontWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });

                _backWedges.Add(new DonutWedge { Id = "vtt", PrimaryLabel = "Web VTT", SubLabel = "VTT", TargetAction = "vtt" });
                _backWedges.Add(new DonutWedge { Id = "srt", PrimaryLabel = "SubRip SRT", SubLabel = "SRT", TargetAction = "srt" });
                _backWedges.Add(new DonutWedge { Id = "txt", PrimaryLabel = "Dialogue Only", SubLabel = "TXT", TargetAction = "txt" });
                _backWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });
            }
            // 5. Archives & Compression (ZIP, 7Z, RAR, TAR, GZ, TGZ, BZ2)
            else if (ext == "zip" || ext == "7z" || ext == "rar" || ext == "tar" || ext == "gz" || ext == "tgz" || ext == "bz2")
            {
                _frontWedges.Add(new DonutWedge { Id = "extract", PrimaryLabel = "Extract All", SubLabel = "Folder", TargetAction = "extract" });
                _frontWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "Recompress ZIP", SubLabel = "ZIP", TargetAction = "zip" });
                _frontWedges.Add(new DonutWedge { Id = "tar", PrimaryLabel = "Convert to TAR", SubLabel = "TAR", TargetAction = "tar" });

                _backWedges.Add(new DonutWedge { Id = "extract", PrimaryLabel = "Extract Here", SubLabel = "Folder", TargetAction = "extract" });
                _backWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "Standard ZIP", SubLabel = "ZIP", TargetAction = "zip" });
                _backWedges.Add(new DonutWedge { Id = "tar", PrimaryLabel = "TAR Archive", SubLabel = "TAR", TargetAction = "tar" });
            }
            // 6. Audio & Video
            else
            {
                _frontWedges.Add(new DonutWedge { Id = "mp4", PrimaryLabel = "MP4", SubLabel = "MP4", TargetAction = "mp4" });
                _frontWedges.Add(new DonutWedge { Id = "mp3", PrimaryLabel = "MP3", SubLabel = "MP3", TargetAction = "mp3" });
                _frontWedges.Add(new DonutWedge { Id = "webm", PrimaryLabel = "WEBM", SubLabel = "WEBM", TargetAction = "webm" });
                _frontWedges.Add(new DonutWedge { Id = "gif", PrimaryLabel = "GIF", SubLabel = "GIF", TargetAction = "gif" });
                _frontWedges.Add(new DonutWedge { Id = "wav", PrimaryLabel = "WAV", SubLabel = "WAV", TargetAction = "wav" });
                _frontWedges.Add(new DonutWedge { Id = "flac", PrimaryLabel = "FLAC", SubLabel = "FLAC", TargetAction = "flac" });
                _frontWedges.Add(new DonutWedge { Id = "m4a", PrimaryLabel = "M4A", SubLabel = "M4A", TargetAction = "m4a" });
                _frontWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });

                _backWedges.Add(new DonutWedge { Id = "mp3", PrimaryLabel = "Extract Audio", SubLabel = "MP3", TargetAction = "mp3" });
                _backWedges.Add(new DonutWedge { Id = "aac", PrimaryLabel = "AAC Audio", SubLabel = "AAC", TargetAction = "aac" });
                _backWedges.Add(new DonutWedge { Id = "ogg", PrimaryLabel = "OGG Audio", SubLabel = "OGG", TargetAction = "ogg" });
                _backWedges.Add(new DonutWedge { Id = "wav", PrimaryLabel = "WAV Audio", SubLabel = "WAV", TargetAction = "wav" });
                _backWedges.Add(new DonutWedge { Id = "flac", PrimaryLabel = "FLAC Lossless", SubLabel = "FLAC", TargetAction = "flac" });
                _backWedges.Add(new DonutWedge { Id = "gif", PrimaryLabel = "GIF Animation", SubLabel = "GIF", TargetAction = "gif" });
                _backWedges.Add(new DonutWedge { Id = "zip", PrimaryLabel = "One ZIP", SubLabel = "ZIP", TargetAction = "zip" });
                _backWedges.Add(new DonutWedge { Id = "mp4", PrimaryLabel = "MP4 Video", SubLabel = "MP4", TargetAction = "mp4" });
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseDonut();
            }
            else if (e.Key == Key.Tab || e.Key == Key.Space || e.Key == Key.F)
            {
                FlipFace();
                e.Handled = true;
            }
        }

        private void OnWindowMouseWheel(object sender, MouseWheelEventArgs e)
        {
            FlipFace();
            e.Handled = true;
        }

        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
            int prevWedge = _hoveredWedgeIndex;
            int prevSubWedge = _hoveredSubWedgeIndex;
            _currentMouse = e.GetPosition(this);
            HitTestDonut(_currentMouse);
            if (_hoveredWedgeIndex != prevWedge || _hoveredSubWedgeIndex != prevSubWedge)
            {
                InvalidateVisual();
            }
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pt = e.GetPosition(this);
            double cx = Width / 2;
            double cy = Height / 2;
            double dist = Math.Sqrt(Math.Pow(pt.X - cx, 2) + Math.Pow(pt.Y - cy, 2));

            // Click center hub -> Flip face between Primary and Secondary wheel
            if (dist <= 65)
            {
                FlipFace();
                e.Handled = true;
                return;
            }
        }

        private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
        {
            Point pt = e.GetPosition(this);
            double cx = Width / 2;
            double cy = Height / 2;
            double dist = Math.Sqrt(Math.Pow(pt.X - cx, 2) + Math.Pow(pt.Y - cy, 2));

            // If released in center hub, keep face flipped, do not close
            if (dist <= 65)
            {
                e.Handled = true;
                return;
            }

            ExecuteHoveredOrClose();
        }

        private void HitTestDonut(Point pt)
        {
            double cx = Width / 2;
            double cy = Height / 2;
            double dx = pt.X - cx;
            double dy = pt.Y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            var activeWedges = _isBackFace ? _backWedges : _frontWedges;
            int count = activeWedges.Count;
            if (count == 0) return;

            // 1. Center Hub (Flip & Cancel Zone)
            if (dist <= 65)
            {
                _hoveredWedgeIndex = -1;
                _hoveredSubWedgeIndex = -1;
                if (_activeSubGroupWedgeIndex >= 0) StartSubArcAnim(false);
                return;
            }

            double angleDeg = (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90.0;
            if (angleDeg < 0) angleDeg += 360.0;

            // 2. Secondary Sub-Arc Outer Ring [218, 330]
            if (dist > 218 && dist <= 330 && _activeSubGroupWedgeIndex >= 0 && _activeSubGroupWedgeIndex < activeWedges.Count)
            {
                var parentWedge = activeWedges[_activeSubGroupWedgeIndex];
                int subCount = parentWedge.SubWedges.Count;
                if (subCount > 0)
                {
                    double sliceDeg = 360.0 / count;
                    double centerA = _activeSubGroupWedgeIndex * sliceDeg;
                    double totalSpan = Math.Max(48.0, subCount * 24.0);

                    double diff = angleDeg - centerA;
                    while (diff > 180.0) diff -= 360.0;
                    while (diff < -180.0) diff += 360.0;

                    if (diff >= -totalSpan / 2.0 && diff <= totalSpan / 2.0)
                    {
                        double normalized = diff + (totalSpan / 2.0);
                        double subSlice = totalSpan / subCount;
                        int sIdx = (int)(normalized / subSlice);
                        if (sIdx < 0) sIdx = 0;
                        if (sIdx >= subCount) sIdx = subCount - 1;

                        _hoveredSubWedgeIndex = sIdx;
                        _hoveredWedgeIndex = _activeSubGroupWedgeIndex;
                        return;
                    }
                    else if (Math.Abs(diff) <= (totalSpan / 2.0) + 14.0)
                    {
                        // Generous catchment buffer near sub-arc edge
                        _hoveredSubWedgeIndex = diff < 0 ? 0 : subCount - 1;
                        _hoveredWedgeIndex = _activeSubGroupWedgeIndex;
                        return;
                    }
                }
            }

            // 3. Main Outer Ring [66, 218]
            if (dist > 65 && dist <= 218)
            {
                double sliceDeg = 360.0 / count;
                double shifted = angleDeg + (sliceDeg / 2.0);
                if (shifted >= 360.0) shifted -= 360.0;

                int idx = (int)(shifted / sliceDeg) % count;
                _hoveredWedgeIndex = idx;
                _hoveredSubWedgeIndex = -1;

                if (idx < activeWedges.Count && activeWedges[idx].SubWedges.Count > 0)
                {
                    if (_activeSubGroupWedgeIndex != idx)
                    {
                        _activeSubGroupWedgeIndex = idx;
                        _subArcAnimProgress = 0.05;
                    }
                    StartSubArcAnim(true);
                }
                else
                {
                    if (_activeSubGroupWedgeIndex >= 0)
                    {
                        StartSubArcAnim(false);
                    }
                }
            }
            else
            {
                // Beyond outer radius or outside sub-arc
                _hoveredWedgeIndex = -1;
                _hoveredSubWedgeIndex = -1;
                if (_activeSubGroupWedgeIndex >= 0) StartSubArcAnim(false);
            }
        }

        private void ExecuteAction(DonutWedge wedge)
        {
            ExecuteTargetAction(wedge.TargetAction, wedge.PrimaryLabel);
        }

        private void ExecuteTargetAction(string targetAction, string actionName)
        {
            if (_selectedFiles.Count == 0)
            {
                _selectedFiles = ExplorerSelectionHelper.GetSelectedFilePaths();
            }

            if (_selectedFiles.Count == 0)
            {
                Notify("Please select a file in File Explorer or Desktop first.");
                return;
            }

            var targetFiles = new List<string>(_selectedFiles);
            var opts = _options;

            // Immediately close radial wheel and show progress ring over cursor
            KeyboardHook.POINT mousePt;
            KeyboardHook.GetCursorPos(out mousePt);
            int cursorX = mousePt.X;
            int cursorY = mousePt.Y;

            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_progressOverlay == null)
                {
                    _progressOverlay = new ProgressFeedbackWindow();
                }
                _progressOverlay.ShowAt(cursorX, cursorY);
                _progressOverlay.UpdateProgress(10);
            }));

            // Execute asynchronously on background thread pool so UI thread NEVER freezes
            System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(delegate
            {
                try
                {
                    try { SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED); } catch { }
                    if (targetAction.Equals("zip", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { if (_progressOverlay != null) _progressOverlay.UpdateProgress(40); }));
                        ExecuteZipCompression(targetFiles);
                        Dispatcher.BeginInvoke(new Action(delegate { if (_progressOverlay != null) _progressOverlay.SetComplete(null); }));
                        if (IsSingleShot)
                        {
                            System.Threading.Thread.Sleep(800);
                            Dispatcher.BeginInvoke(new Action(delegate { System.Windows.Application.Current.Shutdown(); }));
                        }
                        return;
                    }

                    if (targetAction.Equals("merge-pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { if (_progressOverlay != null) _progressOverlay.UpdateProgress(40); }));
                        ExecuteMergePdf(targetFiles);
                        Dispatcher.BeginInvoke(new Action(delegate { if (_progressOverlay != null) _progressOverlay.SetComplete(null); }));
                        if (IsSingleShot)
                        {
                            System.Threading.Thread.Sleep(800);
                            Dispatcher.BeginInvoke(new Action(delegate { System.Windows.Application.Current.Shutdown(); }));
                        }
                        return;
                    }

                    int total = targetFiles.Count;
                    int successCount = 0;
                    string lastOutput = "";
                    string lastError = "";

                    for (int idx = 0; idx < total; idx++)
                    {
                        string f = targetFiles[idx];
                        double segStart = 10.0 + (idx / (double)total) * 85.0;
                        double segEnd = 10.0 + ((idx + 1) / (double)total) * 85.0;
                        object pctLock = new object();
                        int syncProgressPct = (int)segStart;
                        Dispatcher.BeginInvoke(new Action(delegate { if (_progressOverlay != null) _progressOverlay.UpdateProgress(syncProgressPct); }));

                        bool fileFinished = false;
                        using (var ticker = new System.Threading.Timer(delegate(object state)
                        {
                            if (fileFinished) return;
                            lock (pctLock)
                            {
                                if (syncProgressPct < (int)(segEnd - 3))
                                {
                                    syncProgressPct = Math.Min((int)(segEnd - 3), syncProgressPct + 2);
                                    int p = syncProgressPct;
                                    Dispatcher.BeginInvoke(new Action(delegate
                                    {
                                        if (_progressOverlay != null) _progressOverlay.UpdateProgress(p);
                                    }));
                                }
                            }
                        }, null, 120, 120))
                        {
                            var res = FileConversionEngine.Convert(f, targetAction, opts, delegate(int enginePct, string msg)
                            {
                                int mapped = (int)(segStart + (enginePct / 100.0) * (segEnd - segStart));
                                lock (pctLock)
                                {
                                    if (mapped > syncProgressPct)
                                    {
                                        syncProgressPct = mapped;
                                        int p = syncProgressPct;
                                        Dispatcher.BeginInvoke(new Action(delegate
                                        {
                                            if (_progressOverlay != null) _progressOverlay.UpdateProgress(p);
                                        }));
                                    }
                                }
                            });

                            fileFinished = true;
                            if (res.Success)
                            {
                                successCount++;
                                lastOutput = res.OutputPath;
                                syncProgressPct = (int)segEnd;
                                int pEnd = syncProgressPct;
                                Dispatcher.BeginInvoke(new Action(delegate { if (_progressOverlay != null) _progressOverlay.UpdateProgress(pEnd); }));
                            }
                            else
                            {
                                lastError = res.Message;
                            }
                        }
                    }

                    if (successCount > 0)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (_progressOverlay != null)
                            {
                                _progressOverlay.SetComplete(null);
                            }
                        }));

                        if (PlaySoundOnComplete)
                        {
                            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                        }
                        if (ShowNotifications)
                        {
                            Program.SetLastOutput(lastOutput);
                            Notify(string.Format("Done: {0} ({1} file) — Click to open folder", Path.GetFileName(lastOutput), successCount));
                        }
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (_progressOverlay != null)
                            {
                                _progressOverlay.SetFailed(null);
                            }
                        }));
                        Notify(!string.IsNullOrEmpty(lastError) ? lastError : "Action could not complete for selected files.");
                    }

                    if (IsSingleShot)
                    {
                        System.Threading.Thread.Sleep(800);
                        Dispatcher.BeginInvoke(new Action(delegate { System.Windows.Application.Current.Shutdown(); }));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_progressOverlay != null) _progressOverlay.Hide();
                    }));
                    Notify("Action error: " + ex.Message);

                    if (IsSingleShot)
                    {
                        System.Threading.Thread.Sleep(800);
                        Dispatcher.BeginInvoke(new Action(delegate { System.Windows.Application.Current.Shutdown(); }));
                    }
                }
                finally
                {
                    try { SetThreadExecutionState(ES_CONTINUOUS); } catch { }
                }
            }));
        }

        private void ExecuteZipCompression(List<string> files)
        {
            try
            {
                if (files == null || files.Count == 0) return;
                string firstFile = files[0];
                string dir = Path.GetDirectoryName(firstFile);
                if (!string.IsNullOrEmpty(_options.TargetDirectoryMode) && _options.TargetDirectoryMode.Equals("subfolder", StringComparison.OrdinalIgnoreCase))
                {
                    dir = Path.Combine(dir, "Converted");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }

                string outZip;
                if (files.Count == 1)
                {
                    string baseName = Path.GetFileNameWithoutExtension(firstFile);
                    if (firstFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        baseName += "_archive";
                    }
                    outZip = Path.Combine(dir, baseName + ".zip");
                }
                else
                {
                    outZip = Path.Combine(dir, "Archive_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
                }

                if (File.Exists(outZip)) File.Delete(outZip);

                using (var zip = ZipFile.Open(outZip, ZipArchiveMode.Create))
                {
                    foreach (var f in files)
                    {
                        if (File.Exists(f))
                        {
                            zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                        }
                    }
                }

                if (File.Exists(outZip) && new FileInfo(outZip).Length > 0)
                {
                    ShellNotificationHelper.NotifyFileCreated(outZip);
                    Notify("Zipped: " + Path.GetFileName(outZip));
                }
            }
            catch (Exception ex)
            {
                Notify("ZIP error: " + ex.Message);
            }
        }

        private void ExecuteMergePdf(List<string> files)
        {
            try
            {
                if (files == null || files.Count == 0) return;
                string firstFile = files[0];
                string dir = Path.GetDirectoryName(firstFile);
                if (!string.IsNullOrEmpty(_options.TargetDirectoryMode) && _options.TargetDirectoryMode.Equals("subfolder", StringComparison.OrdinalIgnoreCase))
                {
                    dir = Path.Combine(dir, "Converted");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }

                string outPdf = Path.Combine(dir, "Merged_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".pdf");

                var sb = new System.Text.StringBuilder();
                foreach (var f in files)
                {
                    if (File.Exists(f))
                    {
                        sb.AppendLine("--- FILE: " + Path.GetFileName(f) + " ---");
                        string text = File.ReadAllText(f);
                        sb.AppendLine(text);
                        sb.AppendLine();
                    }
                }
                FileConversionEngine.SaveTextAsPdf(sb.ToString(), outPdf, "Merged Document");
                if (File.Exists(outPdf))
                {
                    ShellNotificationHelper.NotifyFileCreated(outPdf);
                    Notify("Merged into " + Path.GetFileName(outPdf));
                }
            }
            catch (Exception ex)
            {
                Notify("Merge error: " + ex.Message);
            }
        }

        private void Notify(string message)
        {
            if (OnStatusMessage != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (OnStatusMessage != null) OnStatusMessage(message);
                }));
            }
        }

        #region Custom Rendering
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double cx = Width / 2;
            double cy = Height / 2;

            // 1. Draw Main Wedges
            DrawWedges(dc, cx, cy);

            // 2. Draw Secondary Expanding Sub-Arcs (Crop, Rotate, Flip)
            DrawSecondarySubArcs(dc, cx, cy);

            // 3. Draw Center Hub
            DrawCenterHub(dc, cx, cy);
        }

        public void RenderToBitmap(string filePath)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double cx = Width / 2;
                double cy = Height / 2;
                DrawWedges(dc, cx, cy);
                DrawSecondarySubArcs(dc, cx, cy);
                DrawCenterHub(dc, cx, cy);
            }
            var rtb = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(filePath))
            {
                enc.Save(fs);
            }
        }

        private void DrawWedges(DrawingContext dc, double cx, double cy)
        {
            var activeWedges = _isBackFace ? _backWedges : _frontWedges;
            int count = activeWedges.Count;
            if (count == 0) return;

            // 0. Draw Machined Chassis Housing (Outer Bezel & Inner Seat Grooves)
            dc.DrawEllipse(CurdTheme.ChassisBezelBrush, CurdTheme.ChassisOuterRimPen, new Point(cx, cy), 221, 221);
            dc.DrawEllipse(null, CurdTheme.ChassisInnerRimPen, new Point(cx, cy), 218, 218);
            dc.DrawEllipse(null, CurdTheme.ChassisInnerRimPen, new Point(cx, cy), 66, 66);

            double sliceDeg = 360.0 / count;

            for (int i = 0; i < count; i++)
            {
                var wedge = activeWedges[i];
                bool isHovered = (i == _hoveredWedgeIndex);

                // Start and End angles (slice 0 centered at 12:00 = 0 deg)
                double centerA = i * sliceDeg;
                double startA = centerA - (sliceDeg / 2.0) + 1.2;
                double endA = centerA + (sliceDeg / 2.0) - 1.2;

                Brush fill = isHovered ? CurdTheme.WedgeHoverBgBrush : CurdTheme.WedgeDefaultBgBrush;
                Pen pen = isHovered ? CurdTheme.WedgeHoverPen : CurdTheme.WedgeDefaultPen;

                Geometry sliceGeo = CreateDonutSliceGeometry(cx, cy, 68, 216, startA, endA);
                dc.DrawGeometry(fill, pen, sliceGeo);

                // 3D Bevel: Specular highlight along top inner curve
                Geometry bevelTop = CreateArcPath(cx, cy, 70, startA + 0.5, endA - 0.5);
                dc.DrawGeometry(null, isHovered ? CurdTheme.WedgeHoverGlowPen : CurdTheme.WedgeBevelTopPen, bevelTop);

                // 3D Bevel: Recessed shadow groove along outer rim
                Geometry bevelBottom = CreateArcPath(cx, cy, 214, startA + 0.5, endA - 0.5);
                dc.DrawGeometry(null, CurdTheme.WedgeBevelBottomPen, bevelBottom);

                // If hovered, draw outer curved indicator bar along the perimeter
                if (isHovered)
                {
                    Geometry arcIndicator = CreateArcPath(cx, cy, 220, startA, endA);
                    dc.DrawGeometry(null, CurdTheme.WedgeIndicatorPen, arcIndicator);
                }

                // Labels placement
                Brush primaryBrush = isHovered ? (CurdTheme.WedgeHoverTextBrush ?? CurdTheme.TextWhiteBrush) : CurdTheme.TextDarkBrush;
                Brush subBrush = isHovered ? (CurdTheme.WedgeHoverSubTextBrush ?? CurdTheme.TextLavenderBrush) : CurdTheme.TextMutedBrush;
                bool deboss = !isHovered || CurdTheme.IsHoverTextWhite;

                Point primaryPt = PolarToCartesian(cx, cy, 142, centerA);
                Point subPt = PolarToCartesian(cx, cy, 166, centerA);

                DrawCenteredText(dc, wedge.PrimaryLabel, primaryPt, 11, true, primaryBrush, deboss);
                DrawCenteredText(dc, wedge.SubLabel, subPt, 9, false, subBrush, deboss);
            }
        }

        private void DrawSecondarySubArcs(DrawingContext dc, double cx, double cy)
        {
            if (_activeSubGroupWedgeIndex < 0 || _subArcAnimProgress < 0.05) return;

            var activeWedges = _isBackFace ? _backWedges : _frontWedges;
            if (_activeSubGroupWedgeIndex >= activeWedges.Count) return;

            var parentWedge = activeWedges[_activeSubGroupWedgeIndex];
            int subCount = parentWedge.SubWedges.Count;
            if (subCount == 0) return;

            double sliceDeg = 360.0 / activeWedges.Count;
            double parentCenterA = _activeSubGroupWedgeIndex * sliceDeg;
            double totalSpan = Math.Max(46.0, subCount * 22.0);
            double subSlice = totalSpan / subCount;

            double innerR = 224;
            double targetOuterR = 308;
            double currentOuterR = innerR + ((targetOuterR - innerR) * _subArcAnimProgress);

            for (int j = 0; j < subCount; j++)
            {
                var subWedge = parentWedge.SubWedges[j];
                bool isSubHovered = (_hoveredSubWedgeIndex == j);

                double subStartA = parentCenterA - (totalSpan / 2.0) + (j * subSlice) + 1.2;
                double subEndA = subStartA + subSlice - 2.4;
                double subCenterA = (subStartA + subEndA) / 2.0;

                Brush fill = isSubHovered ? CurdTheme.WedgeHoverBgBrush : CurdTheme.SubArcDefaultBgBrush;
                Pen pen = isSubHovered ? CurdTheme.WedgeHoverPen : CurdTheme.SubArcDefaultPen;

                Geometry subGeo = CreateDonutSliceGeometry(cx, cy, innerR, currentOuterR, subStartA, subEndA);
                dc.DrawGeometry(fill, pen, subGeo);

                // If hovered, draw glowing outer rim
                if (isSubHovered)
                {
                    Geometry glow = CreateArcPath(cx, cy, currentOuterR + 2, subStartA, subEndA);
                    dc.DrawGeometry(null, CurdTheme.WedgeIndicatorPen, glow);
                }

                // Sub-wedge label
                if (_subArcAnimProgress > 0.5)
                {
                    Brush textBrush = isSubHovered ? (CurdTheme.WedgeHoverTextBrush ?? CurdTheme.TextWhiteBrush) : CurdTheme.TextDarkBrush;
                    bool deboss = !isSubHovered || CurdTheme.IsHoverTextWhite;
                    Point labelPt = PolarToCartesian(cx, cy, innerR + ((currentOuterR - innerR) * 0.52), subCenterA);
                    DrawCenteredText(dc, subWedge.PrimaryLabel, labelPt, 10, true, textBrush, deboss);
                }
            }
        }

        private void DrawCenterHub(DrawingContext dc, double cx, double cy)
        {
            // 0. Machined Bezel Rim & Fluted Inner Channel
            dc.DrawEllipse(CurdTheme.HubBezelBrush, CurdTheme.HubBorderPen, new Point(cx, cy), 67, 67);
            dc.DrawEllipse(null, CurdTheme.HubInnerGroovePen, new Point(cx, cy), 64, 64);

            string ext = "JPG";
            if (_selectedFiles.Count > 0)
            {
                ext = Path.GetExtension(_selectedFiles[0]).TrimStart('.').ToUpperInvariant();
                if (string.IsNullOrEmpty(ext)) ext = "FILE";
            }
            int fileCount = Math.Max(1, _selectedFiles.Count);
            string countHeader = string.Format("{0}  {1}", fileCount, ext);

            var activeWedges = _isBackFace ? _backWedges : _frontWedges;

            // 1. Hub / Switcher State (Cursor inside center hub or outside wheel)
            if (_hoveredWedgeIndex < 0 || _hoveredWedgeIndex >= activeWedges.Count)
            {
                dc.DrawEllipse(CurdTheme.HubCancelBgBrush, CurdTheme.HubCancelPen, new Point(cx, cy), 61, 61);
                DrawGlassLensGlare(dc, cx, cy, 60);

                DrawCenteredText(dc, countHeader, new Point(cx, cy - 22), 10.5, true, CurdTheme.TextDarkBrush);

                string wheelTitle = _isBackFace ? "WHEEL 2: TOOLS" : "WHEEL 1: FORMATS";
                DrawCenteredText(dc, wheelTitle, new Point(cx, cy - 6), 9.5, true, CurdTheme.HubTitleBrush ?? CurdTheme.WedgeHoverBgBrush);

                string tabHint = _isBackFace ? "⟲ Click / Tab: Wheel 1" : "⟲ Click / Tab: Wheel 2";
                DrawCenteredText(dc, tabHint, new Point(cx, cy + 12), 8.5, false, CurdTheme.TextDarkBrush);
                DrawCenteredText(dc, "Esc to cancel", new Point(cx, cy + 26), 8, false, CurdTheme.TextRedBrush);
            }
            // 2. Action Preview State
            else
            {
                dc.DrawEllipse(CurdTheme.HubActionBgBrush, CurdTheme.HubActionPen, new Point(cx, cy), 61, 61);
                DrawGlassLensGlare(dc, cx, cy, 60);

                DrawCenteredText(dc, countHeader, new Point(cx, cy - 18), 9.5, false, CurdTheme.TextMutedBrush);

                string actionLabel;
                // If hovering sub-arc
                if (_hoveredSubWedgeIndex >= 0 && _activeSubGroupWedgeIndex >= 0 && _activeSubGroupWedgeIndex < activeWedges.Count)
                {
                    var parent = activeWedges[_activeSubGroupWedgeIndex];
                    if (_hoveredSubWedgeIndex < parent.SubWedges.Count)
                    {
                        actionLabel = "Drop  " + parent.SubWedges[_hoveredSubWedgeIndex].PrimaryLabel;
                    }
                    else
                    {
                        actionLabel = "Drop  " + parent.PrimaryLabel;
                    }
                }
                else
                {
                    var wedge = activeWedges[_hoveredWedgeIndex];
                    actionLabel = "Drop  " + wedge.PrimaryLabel;
                }

                if (actionLabel.Length > 24) actionLabel = actionLabel.Substring(0, 22) + "..";
                DrawCenteredText(dc, actionLabel, new Point(cx, cy), 11, true, CurdTheme.TextDarkBrush);
                DrawCenteredText(dc, "Release to apply", new Point(cx, cy + 18), 9, false, CurdTheme.TextMutedBrush);
            }
        }

        private void DrawGlassLensGlare(DrawingContext dc, double cx, double cy, double radius)
        {
            var glareGeo = new EllipseGeometry(new Point(cx, cy - (radius * 0.35)), radius * 0.88, radius * 0.50);
            var clipGeo = new EllipseGeometry(new Point(cx, cy), radius, radius);
            dc.PushClip(clipGeo);
            dc.DrawGeometry(CurdTheme.HubGlassGlareBrush, null, glareGeo);
            dc.Pop();
        }

        private static readonly Typeface TypefaceNormal = new Typeface(new FontFamily("Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface TypefaceBold = new Typeface(new FontFamily("Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private void DrawCenteredText(DrawingContext dc, string text, Point center, double fontSize, bool bold, Brush brush, bool debossed = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            var typeface = bold ? TypefaceBold : TypefaceNormal;
            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush,
                1.0
            );

            double x = center.X - (ft.Width / 2.0);
            double y = center.Y - (ft.Height / 2.0);

            if (debossed && CurdTheme.TextDebossShadowBrush != null && brush != CurdTheme.TextRedBrush)
            {
                var ftDeboss = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    CurdTheme.TextDebossShadowBrush,
                    1.0
                );
                dc.DrawText(ftDeboss, new Point(x, y + 1.0));
            }

            dc.DrawText(ft, new Point(x, y));
        }

        private static Point PolarToCartesian(double cx, double cy, double radius, double angleInDegrees)
        {
            double angleRad = (angleInDegrees - 90.0) * Math.PI / 180.0;
            return new Point(cx + radius * Math.Cos(angleRad), cy + radius * Math.Sin(angleRad));
        }

        private static Geometry CreateDonutSliceGeometry(double cx, double cy, double innerRadius, double outerRadius, double startAngle, double endAngle)
        {
            Point pStartOuter = PolarToCartesian(cx, cy, outerRadius, startAngle);
            Point pEndOuter   = PolarToCartesian(cx, cy, outerRadius, endAngle);
            Point pStartInner = PolarToCartesian(cx, cy, innerRadius, startAngle);
            Point pEndInner   = PolarToCartesian(cx, cy, innerRadius, endAngle);

            bool isLargeArc = (endAngle - startAngle) > 180.0;

            var figure = new PathFigure { StartPoint = pStartOuter, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new ArcSegment(pEndOuter, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(pEndInner, true));
            figure.Segments.Add(new ArcSegment(pStartInner, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));

            var pathGeo = new PathGeometry();
            pathGeo.Figures.Add(figure);
            pathGeo.Freeze();
            return pathGeo;
        }

        private static Geometry CreateArcPath(double cx, double cy, double radius, double startAngle, double endAngle)
        {
            Point pStart = PolarToCartesian(cx, cy, radius, startAngle);
            Point pEnd   = PolarToCartesian(cx, cy, radius, endAngle);

            bool isLargeArc = (endAngle - startAngle) > 180.0;

            var figure = new PathFigure { StartPoint = pStart, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(pEnd, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));

            var pathGeo = new PathGeometry();
            pathGeo.Figures.Add(figure);
            pathGeo.Freeze();
            return pathGeo;
        }
        #endregion
    }
}
