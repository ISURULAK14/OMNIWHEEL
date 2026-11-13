using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LocalConvertNative.UI
{
    public class ProgressFeedbackWindow : Window
    {
        private int _percent = 0;
        private double _displayPercent = 0.0;
        private double _targetPercent = 0.0;
        private DispatcherTimer _smoothTimer;
        private bool _isCompleted = false;
        private bool _isFailed = false;
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

        public ProgressFeedbackWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            Width = 96;
            Height = 96;

            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

            _smoothTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _smoothTimer.Tick += (s, e) =>
            {
                double diff = _targetPercent - _displayPercent;
                if (Math.Abs(diff) < 0.3)
                {
                    _displayPercent = _targetPercent;
                    _percent = (int)Math.Round(_displayPercent);
                    _smoothTimer.Stop();
                }
                else
                {
                    _displayPercent += diff * 0.30;
                    _percent = (int)Math.Round(_displayPercent);
                }
                InvalidateVisual();
            };
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

                // Win11: Disable DWM rounded corner clipping on circular HUD
                int cornerPreference = 1; // DWMWCP_DONOTROUND
                DwmSetWindowAttribute(helper.Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        public void ShowAt(double screenX, double screenY)
        {
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

            Left = (screenX / scaleX) - (Width / 2.0);
            Top = (screenY / scaleY) - (Height / 2.0);
            if (_smoothTimer != null) _smoothTimer.Stop();
            _percent = 0;
            _displayPercent = 0.0;
            _targetPercent = 0.0;
            _isCompleted = false;
            _isFailed = false;
            Opacity = 0.0;
            Show();
            var anim = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(100)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(UIElement.OpacityProperty, anim);
            InvalidateVisual();
        }

        public void UpdateProgress(int percent)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateProgress(percent)));
                return;
            }

            _targetPercent = Math.Max(0, Math.Min(100, percent));
            if (_smoothTimer != null && !_smoothTimer.IsEnabled)
            {
                _smoothTimer.Start();
            }
        }

        public void SetComplete(Action onFadedOut = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetComplete(onFadedOut)));
                return;
            }

            if (_smoothTimer != null) _smoothTimer.Stop();
            _displayPercent = 100.0;
            _targetPercent = 100.0;
            _percent = 100;
            _isCompleted = true;
            InvalidateVisual();

            // Smooth fade-out after 350ms pause
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fadeAnim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(450)));
                fadeAnim.Completed += (asnd, aev) =>
                {
                    Hide();
                    if (onFadedOut != null) onFadedOut();
                };
                BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            };
            timer.Start();
        }

        public void SetFailed(Action onFadedOut = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetFailed(onFadedOut)));
                return;
            }

            if (_smoothTimer != null) _smoothTimer.Stop();
            _isFailed = true;
            _isCompleted = false;
            InvalidateVisual();

            // Smooth fade-out after 600ms pause so user notices failure
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fadeAnim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(450)));
                fadeAnim.Completed += (asnd, aev) =>
                {
                    Hide();
                    if (onFadedOut != null) onFadedOut();
                };
                BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            };
            timer.Start();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double cx = Width / 2.0;
            double cy = Height / 2.0;
            double radius = 38.0;

            if (_isFailed)
            {
                // Red failure circle badge (#EF4444)
                var redBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                redBrush.Freeze();
                dc.DrawEllipse(redBrush, null, new Point(cx, cy), radius, radius);

                // White Cross (✕)
                var crossPen = new Pen(Brushes.White, 4.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                crossPen.Freeze();

                double offset = 10.0;
                dc.DrawLine(crossPen, new Point(cx - offset, cy - offset), new Point(cx + offset, cy + offset));
                dc.DrawLine(crossPen, new Point(cx + offset, cy - offset), new Point(cx - offset, cy + offset));
            }
            else if (_isCompleted)
            {
                // Green completion circle badge
                var greenBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // #10B981
                greenBrush.Freeze();
                dc.DrawEllipse(greenBrush, null, new Point(cx, cy), radius, radius);

                // White Checkmark (✓)
                var checkPen = new Pen(Brushes.White, 4.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                checkPen.Freeze();

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(cx - 13, cy + 1), false, false);
                    ctx.LineTo(new Point(cx - 4, cy + 11), true, true);
                    ctx.LineTo(new Point(cx + 14, cy - 9), true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(null, checkPen, geo);
            }
            else
            {
                // Soft ambient white backdrop with shadow
                var bgBrush = new SolidColorBrush(Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF));
                bgBrush.Freeze();
                var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 1.0);
                shadowPen.Freeze();
                dc.DrawEllipse(bgBrush, shadowPen, new Point(cx, cy), radius + 2, radius + 2);

                // Lavender background track
                var trackPen = new Pen(new SolidColorBrush(Color.FromRgb(0xED, 0xE9, 0xFE)), 4.5);
                trackPen.Freeze();
                dc.DrawEllipse(null, trackPen, new Point(cx, cy), radius - 4, radius - 4);

                // Vibrant Purple Progress Arc (#7C3AED)
                if (_displayPercent > 0.01)
                {
                    var progressPen = new Pen(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), 4.5)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    progressPen.Freeze();

                    double sweepDeg = (_displayPercent / 100.0) * 360.0;
                    Geometry arcGeo = CreateArcPath(cx, cy, radius - 4, -90.0, -90.0 + sweepDeg);
                    dc.DrawGeometry(null, progressPen, arcGeo);
                }

                // Percentage text
                string label = ((int)Math.Round(_displayPercent)) + "%";
                var ft = new FormattedText(
                    label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    13.5,
                    new SolidColorBrush(Color.FromRgb(0x4C, 0x1D, 0x95)),
                    1.0
                );
                dc.DrawText(ft, new Point(cx - (ft.Width / 2.0), cy - (ft.Height / 2.0)));
            }
        }

        private static Geometry CreateArcPath(double cx, double cy, double radius, double startAngle, double endAngle)
        {
            if (Math.Abs(endAngle - startAngle) >= 359.99)
            {
                var eg = new EllipseGeometry(new Point(cx, cy), radius, radius);
                eg.Freeze();
                return eg;
            }

            double startRad = startAngle * Math.PI / 180.0;
            double endRad = endAngle * Math.PI / 180.0;

            Point pStart = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
            Point pEnd = new Point(cx + radius * Math.Cos(endRad), cy + radius * Math.Sin(endRad));

            bool isLargeArc = Math.Abs(endAngle - startAngle) > 180.0;

            var figure = new PathFigure { StartPoint = pStart, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(pEnd, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));

            var pathGeo = new PathGeometry();
            pathGeo.Figures.Add(figure);
            pathGeo.Freeze();
            return pathGeo;
        }
    }
}
