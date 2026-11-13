using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Data;
using System.Windows.Controls.Primitives;

namespace LocalConvertNative.UI
{
    public class SettingsWindow : Window
    {
        private AppSettings _settings;

        // Theme Mode Rocker Buttons
        private Button _btnThemeDark;
        private Button _btnThemeLight;

        // Hotkey & Trigger
        private ComboBox _cmbHotkey;
        private ComboBox _cmbSummonMode;
        private CheckBox _chkOnlyExplorer;
        private CheckBox _chkSpacePeek;

        // Secondary Wheels & Layer Switching
        private CheckBox _chkWheelFlip;
        private CheckBox _chkKeyFlip;
        private ComboBox _cmbDefaultLayer;

        // Quality & Output
        private Slider _sliderQuality;
        private TextBlock _lblQualityVal;
        private ComboBox _cmbPng;
        private ComboBox _cmbPdf;
        private RadioButton _rbSourceDir;
        private RadioButton _rbSubDir;

        // Media
        private ComboBox _cmbAudioBitrate;
        private ComboBox _cmbGifFps;

        // Appearance & System
        private ComboBox _cmbTheme;
        private ComboBox _cmbScale;
        private CheckBox _chkStartup;
        private CheckBox _chkNotifications;
        private CheckBox _chkSound;

        // Live Theming Trackers
        private Border _mainBorder;
        private Border _headerBorder;
        private Border _footerBorder;
        private TextBlock _titleText;
        private TextBlock _titleIcon;
        private Button _closeBtn;
        private List<Border> _sectionCards = new List<Border>();
        private List<TextBlock> _sectionHeaders = new List<TextBlock>();
        private List<TextBlock> _fieldLabels = new List<TextBlock>();
        private List<CheckBox> _checkboxes = new List<CheckBox>();
        private List<RadioButton> _radios = new List<RadioButton>();
        private List<ComboBox> _combos = new List<ComboBox>();
        private Button _btnSave;
        private Button _btnCancel;
        private string _initialThemeMode;
        private string _initialThemeAccent;
        private bool _isSaved = false;

        public event Action<AppSettings> OnSettingsSaved;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmIsCompositionEnabled(out bool enabled);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            bool isDwm = true;
            try { DwmIsCompositionEnabled(out isDwm); } catch { }
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var src = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            if (src != null)
            {
                src.AddHook(WndProc);
            }
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int HTMAXBUTTON = 9;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
        }

        public SettingsWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Width = 510;
            Height = 670;
            Title = "OMNIWHEEL — Control Deck";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath));
                }
            }
            catch { }

            _settings = AppSettings.Load();
            _initialThemeMode = _settings.ThemeMode;
            _initialThemeAccent = _settings.ThemeAccent;

            CurdTheme.SetTheme(_settings.ThemeAccent, _settings.ThemeMode);

            BuildUI();
            ApplyThemeStyling();

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };

            Closed += (s, e) =>
            {
                if (!_isSaved)
                {
                    CurdTheme.SetTheme(_initialThemeAccent, _initialThemeMode);
                }
            };
        }

        private void BuildUI()
        {
            _mainBorder = new Border
            {
                BorderThickness = new Thickness(1.8),
                CornerRadius = new CornerRadius(14),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 10,
                    Opacity = 0.55,
                    BlurRadius = 26
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) }); // Header
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Footer

            // --- 1. HEADER (Machined Faceplate) ---
            _headerBorder = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                BorderThickness = new Thickness(0, 0, 0, 1.2),
                Padding = new Thickness(18, 0, 14, 0)
            };
            _headerBorder.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var headerDock = new DockPanel();
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _titleIcon = new TextBlock
            {
                Text = "☸  ",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(_titleIcon);

            _titleText = new TextBlock
            {
                Text = "OMNIWHEEL — CONTROL DECK",
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(_titleText);

            DockPanel.SetDock(titleStack, Dock.Left);
            headerDock.Children.Add(titleStack);

            _closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            _closeBtn.Click += (s, e) => Close();
            DockPanel.SetDock(_closeBtn, Dock.Right);
            headerDock.Children.Add(_closeBtn);

            _headerBorder.Child = headerDock;
            Grid.SetRow(_headerBorder, 0);
            grid.Children.Add(_headerBorder);

            // --- 2. BODY CONTENT (Skeuomorphic Instrument Bays) ---
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };
            var contentStack = new StackPanel();

            // Section 1: Activation & Hotkey
            var sec1 = CreateSectionCard();
            sec1.Child = BuildActivationSection();
            contentStack.Children.Add(sec1);

            // Section 2: Secondary Wheels
            var sec2 = CreateSectionCard();
            sec2.Child = BuildSecondaryWheelsSection();
            contentStack.Children.Add(sec2);

            // Section 3: Image & Document Quality
            var sec3 = CreateSectionCard();
            sec3.Child = BuildQualitySection();
            contentStack.Children.Add(sec3);

            // Section 4: Media Options
            var sec4 = CreateSectionCard();
            sec4.Child = BuildMediaSection();
            contentStack.Children.Add(sec4);

            // Section 5: Visual Theme & Skeuomorphism
            var sec5 = CreateSectionCard();
            sec5.Child = BuildVisualThemeSection();
            contentStack.Children.Add(sec5);

            scroll.Content = contentStack;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // --- 3. FOOTER (Tactile Control Bar) ---
            _footerBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 12, 12),
                BorderThickness = new Thickness(0, 1.2, 0, 0),
                Padding = new Thickness(18, 12, 18, 12)
            };
            var btnStack = new StackPanel
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
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            _btnCancel.Click += (s, e) => Close();
            btnStack.Children.Add(_btnCancel);

            _btnSave = new Button
            {
                Content = "Save & Apply",
                Width = 125,
                Height = 34,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            _btnSave.Click += (s, e) => SaveSettings();
            btnStack.Children.Add(_btnSave);

            _footerBorder.Child = btnStack;
            Grid.SetRow(_footerBorder, 2);
            grid.Children.Add(_footerBorder);

            _mainBorder.Child = grid;
            Content = _mainBorder;
        }

        private Border CreateSectionCard()
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(14, 12, 14, 14)
            };
            _sectionCards.Add(card);
            return card;
        }

        private UIElement BuildActivationSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(CreateSectionHeader("ACTIVATION & HOTKEY TRIGGER"));

            sp.Children.Add(CreateFieldLabel("Primary Activation Hotkey:"));
            _cmbHotkey = CreateStyledComboBox();
            _cmbHotkey.Items.Add("` (Backtick / Tilde key) [Default]");
            _cmbHotkey.Items.Add("Ctrl + Space");
            _cmbHotkey.Items.Add("Alt + D");
            _cmbHotkey.Items.Add("F4");
            _cmbHotkey.SelectedIndex = _settings.PrimaryHotkey == "CtrlSpace" ? 1 :
                                       _settings.PrimaryHotkey == "AltD" ? 2 :
                                       _settings.PrimaryHotkey == "F4" ? 3 : 0;
            sp.Children.Add(_cmbHotkey);

            sp.Children.Add(CreateFieldLabel("Summon Interaction Mode:"));
            _cmbSummonMode = CreateStyledComboBox();
            _cmbSummonMode.Items.Add("Dual Mode (Tap to Pin / Hold to Drop) [Default]");
            _cmbSummonMode.Items.Add("Tap to Toggle (Click/Esc to close)");
            _cmbSummonMode.Items.Add("Push-to-Talk (Hold key, release to apply)");
            _cmbSummonMode.SelectedIndex = _settings.SummonMode == "TapToggle" ? 1 :
                                           _settings.SummonMode == "PushToTalk" ? 2 : 0;
            sp.Children.Add(_cmbSummonMode);

            _chkOnlyExplorer = CreateStyledCheckBox("Only summon when File Explorer or Desktop is foreground", _settings.OnlyInExplorer);
            _chkOnlyExplorer.Margin = new Thickness(2, 6, 0, 2);
            sp.Children.Add(_chkOnlyExplorer);

            _chkSpacePeek = CreateStyledCheckBox("Space bar Quick Preview (Peek / QuickLook in File Explorer)", _settings.EnableSpacePeek);
            _chkSpacePeek.Margin = new Thickness(2, 4, 0, 2);
            sp.Children.Add(_chkSpacePeek);

            return sp;
        }

        private UIElement BuildSecondaryWheelsSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(CreateSectionHeader("SECONDARY WHEELS & LAYER SWITCHING"));

            _chkWheelFlip = CreateStyledCheckBox("Mouse Scroll Wheel flips layers (Wheel 1 ⇄ Wheel 2)", _settings.EnableMouseWheelFlip);
            _chkWheelFlip.Margin = new Thickness(2, 2, 0, 6);
            sp.Children.Add(_chkWheelFlip);

            _chkKeyFlip = CreateStyledCheckBox("Tab, Space, or F key flips layers while wheel is open", _settings.EnableKeyFlip);
            _chkKeyFlip.Margin = new Thickness(2, 0, 0, 8);
            sp.Children.Add(_chkKeyFlip);

            sp.Children.Add(CreateFieldLabel("Default Initial Wheel Layer on Summon:"));
            _cmbDefaultLayer = CreateStyledComboBox();
            _cmbDefaultLayer.Items.Add("Wheel 1: Primary File Formats (PNG, WEBP, PDF, SVG, etc.)");
            _cmbDefaultLayer.Items.Add("Wheel 2: Creative Tools & Edits (Crop, Rotate, Flip, Live Photo, etc.)");
            _cmbDefaultLayer.SelectedIndex = _settings.DefaultWheelLayer == "Secondary" ? 1 : 0;
            sp.Children.Add(_cmbDefaultLayer);

            return sp;
        }

        private UIElement BuildQualitySection()
        {
            var sp = new StackPanel();
            sp.Children.Add(CreateSectionHeader("IMAGE & DOCUMENT ENCODING"));

            var sliderPanel = new DockPanel { Margin = new Thickness(2, 2, 2, 2) };
            var lblQuality = CreateFieldLabel("JPEG / WEBP Quality:");
            DockPanel.SetDock(lblQuality, Dock.Left);
            sliderPanel.Children.Add(lblQuality);

            _lblQualityVal = new TextBlock
            {
                Text = _settings.JpegQuality + "%",
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(_lblQualityVal, Dock.Right);
            sliderPanel.Children.Add(_lblQualityVal);
            sp.Children.Add(sliderPanel);

            _sliderQuality = new Slider
            {
                Minimum = 60,
                Maximum = 100,
                Value = _settings.JpegQuality,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(2, 4, 2, 10)
            };
            _sliderQuality.ValueChanged += (s, e) =>
            {
                if (_lblQualityVal != null) _lblQualityVal.Text = (int)_sliderQuality.Value + "%";
            };
            sp.Children.Add(_sliderQuality);

            sp.Children.Add(CreateFieldLabel("PNG Optimization Level:"));
            _cmbPng = CreateStyledComboBox();
            _cmbPng.Items.Add("Standard (Fast bicubic compression)");
            _cmbPng.Items.Add("Maximum Optimization (Lossless deflater)");
            _cmbPng.SelectedIndex = _settings.PngOptimization == "Maximum" ? 1 : 0;
            sp.Children.Add(_cmbPng);

            sp.Children.Add(CreateFieldLabel("PDF Preset:"));
            _cmbPdf = CreateStyledComboBox();
            _cmbPdf.Items.Add("Standard Compression (Default)");
            _cmbPdf.Items.Add("High Quality Print (Uncompressed vectors)");
            _cmbPdf.Items.Add("Minimal File Size (Compressed streams)");
            _cmbPdf.SelectedIndex = _settings.PdfPreset == "HighQuality" ? 1 : (_settings.PdfPreset == "MinimalSize" ? 2 : 0);
            sp.Children.Add(_cmbPdf);

            sp.Children.Add(CreateFieldLabel("Converted Output Destination:"));
            _rbSourceDir = CreateStyledRadio("Save converted files in the same folder as source", _settings.TargetDirectoryMode != "subfolder");
            _rbSourceDir.Margin = new Thickness(2, 2, 0, 4);
            _rbSubDir = CreateStyledRadio("Save into dedicated \"./Converted/\" subfolder", _settings.TargetDirectoryMode == "subfolder");
            _rbSubDir.Margin = new Thickness(2, 0, 0, 2);
            sp.Children.Add(_rbSourceDir);
            sp.Children.Add(_rbSubDir);

            return sp;
        }

        private UIElement BuildMediaSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(CreateSectionHeader("AUDIO & MEDIA TRANSCODE"));

            sp.Children.Add(CreateFieldLabel("MP3 Audio Bitrate:"));
            _cmbAudioBitrate = CreateStyledComboBox();
            _cmbAudioBitrate.Items.Add("128 kbps (Draft / Podcast)");
            _cmbAudioBitrate.Items.Add("192 kbps (Standard CD Quality) [Default]");
            _cmbAudioBitrate.Items.Add("256 kbps (High Fidelity)");
            _cmbAudioBitrate.Items.Add("320 kbps (Maximum Master Quality)");
            _cmbAudioBitrate.SelectedIndex = _settings.AudioBitrate == 128 ? 0 :
                                             _settings.AudioBitrate == 256 ? 2 :
                                             _settings.AudioBitrate == 320 ? 3 : 1;
            sp.Children.Add(_cmbAudioBitrate);

            sp.Children.Add(CreateFieldLabel("GIF Animation Framerate:"));
            _cmbGifFps = CreateStyledComboBox();
            _cmbGifFps.Items.Add("10 fps (Compact File Size) [Default]");
            _cmbGifFps.Items.Add("15 fps (Smooth Animation)");
            _cmbGifFps.Items.Add("24 fps (Ultra Fluid Motion)");
            _cmbGifFps.SelectedIndex = _settings.GifFramerate == 15 ? 1 :
                                       _settings.GifFramerate == 24 ? 2 : 0;
            sp.Children.Add(_cmbGifFps);

            return sp;
        }

        private UIElement BuildVisualThemeSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(CreateSectionHeader("SKEUOMORPHISM & THEME SELECTION"));

            // --- TACTILE SKEUOMORPHIC THEME MODE SWITCHER ---
            sp.Children.Add(CreateFieldLabel("Interface Theme Mode (Light / Dark):"));

            var rockerGrid = new Grid { Margin = new Thickness(2, 4, 2, 12) };
            rockerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rockerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnThemeDark = new Button
            {
                Content = "🌙  Dark Mode",
                Height = 36,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            _btnThemeDark.Click += (s, e) =>
            {
                _settings.ThemeMode = "Dark";
                ApplyThemeStyling();
            };
            Grid.SetColumn(_btnThemeDark, 0);
            rockerGrid.Children.Add(_btnThemeDark);

            _btnThemeLight = new Button
            {
                Content = "☀️  Light Mode",
                Height = 36,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            _btnThemeLight.Click += (s, e) =>
            {
                _settings.ThemeMode = "Light";
                ApplyThemeStyling();
            };
            Grid.SetColumn(_btnThemeLight, 1);
            rockerGrid.Children.Add(_btnThemeLight);

            sp.Children.Add(rockerGrid);

            // Wheel Accent Color
            sp.Children.Add(CreateFieldLabel("Machined Wheel Accent:"));
            _cmbTheme = CreateStyledComboBox();
            _cmbTheme.Items.Add("Royal Purple (Official OMNIWHEEL Theme) [Default]");
            _cmbTheme.Items.Add("Dark Slate (Obsidian Stealth)");
            _cmbTheme.Items.Add("Emerald Green (Precision Jade)");
            _cmbTheme.Items.Add("Cobalt Blue (Windows Precision)");
            _cmbTheme.Items.Add("Sunset Amber (Industrial Brass)");
            _cmbTheme.Items.Add("Pastel Lavender (Soft Lilac)");
            _cmbTheme.Items.Add("Pastel Mint (Fresh Sage)");
            _cmbTheme.Items.Add("Pastel Rose (Blush Coral)");
            _cmbTheme.Items.Add("Pastel Sky (Powder Azure)");
            _cmbTheme.Items.Add("Pastel Peach (Warm Apricot)");
            _cmbTheme.Items.Add("Pastel Matcha (Fresh Celadon)");
            _cmbTheme.Items.Add("Nordic Frost (Clean Minimal Slate)");
            _cmbTheme.Items.Add("Monochrome Slate (Pure Graphite)");
            _cmbTheme.Items.Add("Solar Gold (Clean Sunburst)");
            _cmbTheme.Items.Add("Ocean Deep (Clean Marine)");
            _cmbTheme.SelectedIndex = _settings.ThemeAccent == "DarkSlate" ? 1 :
                                      _settings.ThemeAccent == "Emerald" ? 2 :
                                      _settings.ThemeAccent == "Cobalt" ? 3 :
                                      _settings.ThemeAccent == "Amber" ? 4 :
                                      _settings.ThemeAccent == "PastelLavender" ? 5 :
                                      _settings.ThemeAccent == "PastelMint" ? 6 :
                                      _settings.ThemeAccent == "PastelRose" ? 7 :
                                      _settings.ThemeAccent == "PastelSky" ? 8 :
                                      _settings.ThemeAccent == "PastelPeach" ? 9 :
                                      _settings.ThemeAccent == "PastelMatcha" ? 10 :
                                      _settings.ThemeAccent == "NordicFrost" ? 11 :
                                      _settings.ThemeAccent == "Monochrome" ? 12 :
                                      _settings.ThemeAccent == "Solar" ? 13 :
                                      _settings.ThemeAccent == "Ocean" ? 14 : 0;
            _cmbTheme.SelectionChanged += (s, e) =>
            {
                _settings.ThemeAccent = GetSelectedAccent();
                ApplyThemeStyling();
            };
            sp.Children.Add(_cmbTheme);

            // Wheel Scale
            sp.Children.Add(CreateFieldLabel("Wheel Size Scale:"));
            _cmbScale = CreateStyledComboBox();
            _cmbScale.Items.Add("Standard 100% (Default)");
            _cmbScale.Items.Add("Compact 85% (Small displays)");
            _cmbScale.Items.Add("Large 115% (High-res 4K screens)");
            _cmbScale.SelectedIndex = _settings.WheelScale == "Compact" ? 1 :
                                      _settings.WheelScale == "Large" ? 2 : 0;
            sp.Children.Add(_cmbScale);

            // System Toggles
            _chkStartup = CreateStyledCheckBox("Start automatically with Windows (Background service)", _settings.StartWithWindows);
            _chkStartup.Margin = new Thickness(2, 4, 0, 4);
            sp.Children.Add(_chkStartup);

            _chkNotifications = CreateStyledCheckBox("Show notification balloon on conversion complete", _settings.ShowNotifications);
            _chkNotifications.Margin = new Thickness(2, 0, 0, 4);
            sp.Children.Add(_chkNotifications);

            _chkSound = CreateStyledCheckBox("Play subtle completion chime sound", _settings.PlaySoundOnComplete);
            _chkSound.Margin = new Thickness(2, 0, 0, 2);
            sp.Children.Add(_chkSound);

            return sp;
        }

        private TextBlock CreateSectionHeader(string title)
        {
            var tb = new TextBlock
            {
                Text = title,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(2, 0, 0, 8)
            };
            _sectionHeaders.Add(tb);
            return tb;
        }

        private TextBlock CreateFieldLabel(string title)
        {
            var tb = new TextBlock
            {
                Text = title,
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(2, 0, 0, 4)
            };
            _fieldLabels.Add(tb);
            return tb;
        }

        private static ControlTemplate _cachedComboBoxTemplate;
        private static ControlTemplate GetComboBoxTemplate()
        {
            if (_cachedComboBoxTemplate != null) return _cachedComboBoxTemplate;

            var template = new ControlTemplate(typeof(ComboBox));
            var gridFactory = new FrameworkElementFactory(typeof(Grid));

            // ToggleButton
            var toggleFactory = new FrameworkElementFactory(typeof(ToggleButton), "ToggleButton");
            toggleFactory.SetValue(ToggleButton.FocusableProperty, false);
            toggleFactory.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            toggleFactory.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding("IsDropDownOpen") { Mode = BindingMode.TwoWay, RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            // ToggleButton Template
            var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            var borderFactory = new FrameworkElementFactory(typeof(Border), "Border");
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ComboBox.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(ComboBox.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1.2));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));

            var tbGridFactory = new FrameworkElementFactory(typeof(Grid));
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(24));
            tbGridFactory.AppendChild(col0);
            tbGridFactory.AppendChild(col1);

            var arrowFactory = new FrameworkElementFactory(typeof(Path));
            arrowFactory.SetValue(Grid.ColumnProperty, 1);
            arrowFactory.SetValue(Path.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrowFactory.SetValue(Path.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrowFactory.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0 Z"));
            arrowFactory.SetValue(Path.FillProperty, new TemplateBindingExtension(ComboBox.ForegroundProperty));
            tbGridFactory.AppendChild(arrowFactory);

            borderFactory.AppendChild(tbGridFactory);
            toggleTemplate.VisualTree = borderFactory;
            toggleFactory.SetValue(ToggleButton.TemplateProperty, toggleTemplate);
            gridFactory.AppendChild(toggleFactory);

            // ContentPresenter
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter), "ContentSite");
            contentFactory.SetValue(ContentPresenter.IsHitTestVisibleProperty, false);
            contentFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            contentFactory.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            contentFactory.SetValue(ContentPresenter.MarginProperty, new Thickness(8, 3, 24, 3));
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            gridFactory.AppendChild(contentFactory);

            // Popup
            var popupFactory = new FrameworkElementFactory(typeof(Popup), "Popup");
            popupFactory.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popupFactory.SetValue(Popup.IsOpenProperty, new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));
            popupFactory.SetValue(Popup.AllowsTransparencyProperty, true);
            popupFactory.SetValue(Popup.FocusableProperty, false);
            popupFactory.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Slide);

            var dropDownGrid = new FrameworkElementFactory(typeof(Grid), "DropDown");
            dropDownGrid.SetValue(Grid.MinWidthProperty, new TemplateBindingExtension(ComboBox.ActualWidthProperty));
            dropDownGrid.SetValue(Grid.MaxHeightProperty, new TemplateBindingExtension(ComboBox.MaxDropDownHeightProperty));

            var dropDownBorder = new FrameworkElementFactory(typeof(Border), "DropDownBorder");
            dropDownBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ComboBox.BackgroundProperty));
            dropDownBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(ComboBox.BorderBrushProperty));
            dropDownBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1.2));
            dropDownBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            dropDownBorder.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 4));

            var shadow = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 3, Opacity = 0.4, Direction = 270 };
            dropDownBorder.SetValue(Border.EffectProperty, shadow);

            var scrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            var itemsHostFactory = new FrameworkElementFactory(typeof(StackPanel));
            itemsHostFactory.SetValue(StackPanel.IsItemsHostProperty, true);
            scrollFactory.AppendChild(itemsHostFactory);
            dropDownBorder.AppendChild(scrollFactory);
            dropDownGrid.AppendChild(dropDownBorder);
            popupFactory.AppendChild(dropDownGrid);
            gridFactory.AppendChild(popupFactory);

            template.VisualTree = gridFactory;
            _cachedComboBoxTemplate = template;
            return template;
        }

        private ComboBox CreateStyledComboBox()
        {
            var cmb = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(2, 0, 2, 8),
                FontSize = 11.5,
                Template = GetComboBoxTemplate(),
                Cursor = Cursors.Hand
            };
            _combos.Add(cmb);
            return cmb;
        }

        private CheckBox CreateStyledCheckBox(string content, bool isChecked)
        {
            var chk = new CheckBox
            {
                Content = content,
                IsChecked = isChecked,
                FontSize = 11.5
            };
            _checkboxes.Add(chk);
            return chk;
        }

        private RadioButton CreateStyledRadio(string content, bool isChecked)
        {
            var rb = new RadioButton
            {
                Content = content,
                IsChecked = isChecked,
                FontSize = 11.5
            };
            _radios.Add(rb);
            return rb;
        }

        private string GetSelectedAccent()
        {
            if (_cmbTheme == null) return "Purple";
            switch (_cmbTheme.SelectedIndex)
            {
                case 1: return "DarkSlate";
                case 2: return "Emerald";
                case 3: return "Cobalt";
                case 4: return "Amber";
                case 5: return "PastelLavender";
                case 6: return "PastelMint";
                case 7: return "PastelRose";
                case 8: return "PastelSky";
                case 9: return "PastelPeach";
                case 10: return "PastelMatcha";
                case 11: return "NordicFrost";
                case 12: return "Monochrome";
                case 13: return "Solar";
                case 14: return "Ocean";
                default: return "Purple";
            }
        }

        private void ApplyThemeStyling()
        {
            CurdTheme.SetTheme(_settings.ThemeAccent, _settings.ThemeMode);
            bool isDark = CurdTheme.IsDark;

            // Chassis
            if (_mainBorder != null)
            {
                _mainBorder.Background = CurdTheme.WindowBgBrush;
                _mainBorder.BorderBrush = CurdTheme.WindowBorderBrush;
            }

            // Header
            if (_headerBorder != null)
            {
                _headerBorder.Background = CurdTheme.HeaderBgBrush;
                _headerBorder.BorderBrush = CurdTheme.HeaderBorderBrush;
            }
            if (_titleIcon != null) _titleIcon.Foreground = CurdTheme.FieldHeaderBrush;
            if (_titleText != null) _titleText.Foreground = CurdTheme.HeaderTitleBrush;

            if (_closeBtn != null)
            {
                _closeBtn.Background = CurdTheme.ButtonCancelBgBrush;
                _closeBtn.BorderBrush = CurdTheme.ButtonCancelBorderBrush;
                _closeBtn.Foreground = CurdTheme.ButtonCancelTextBrush;
            }

            // Footer
            if (_footerBorder != null)
            {
                _footerBorder.Background = CurdTheme.HeaderBgBrush;
                _footerBorder.BorderBrush = CurdTheme.HeaderBorderBrush;
            }

            // Section Cards
            foreach (var card in _sectionCards)
            {
                card.Background = CurdTheme.CardWellBgBrush;
                card.BorderBrush = CurdTheme.CardWellBorderBrush;
            }

            // Headers & Labels
            foreach (var sh in _sectionHeaders) sh.Foreground = CurdTheme.FieldHeaderBrush;
            foreach (var fl in _fieldLabels) fl.Foreground = CurdTheme.FieldLabelBrush;

            // Form Controls & CheckBoxes
            foreach (var chk in _checkboxes)
            {
                chk.Foreground = CurdTheme.FieldTextBrush;
                chk.Cursor = Cursors.Hand;
            }
            foreach (var rb in _radios)
            {
                rb.Foreground = CurdTheme.FieldTextBrush;
                rb.Cursor = Cursors.Hand;
            }

            // ComboBox Dropdown Item Skeuomorphic Style
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, CurdTheme.FieldBgBrush));
            itemStyle.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, CurdTheme.FieldTextBrush));
            itemStyle.Setters.Add(new Setter(ComboBoxItem.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(8, 5, 8, 5)));
            itemStyle.Setters.Add(new Setter(ComboBoxItem.CursorProperty, Cursors.Hand));

            var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, CurdTheme.ButtonAccentBgBrush));
            hoverTrigger.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, CurdTheme.ButtonAccentTextBrush ?? Brushes.White));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selectTrigger.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, CurdTheme.ButtonAccentBgBrush));
            selectTrigger.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, CurdTheme.ButtonAccentTextBrush ?? Brushes.White));
            itemStyle.Triggers.Add(selectTrigger);

            foreach (var cmb in _combos)
            {
                cmb.Background = CurdTheme.FieldBgBrush;
                cmb.BorderBrush = CurdTheme.FieldBorderBrush;
                cmb.Foreground = CurdTheme.FieldTextBrush;
                cmb.ItemContainerStyle = itemStyle;
            }

            // Slider & Badge
            if (_sliderQuality != null)
            {
                _sliderQuality.Foreground = CurdTheme.FieldHeaderBrush;
                _sliderQuality.Background = CurdTheme.SliderTrackBgBrush;
            }
            if (_lblQualityVal != null) _lblQualityVal.Foreground = CurdTheme.FieldHeaderBrush;

            // Buttons
            if (_btnSave != null)
            {
                _btnSave.Background = CurdTheme.ButtonAccentBgBrush;
                _btnSave.BorderBrush = CurdTheme.ButtonAccentBorderBrush;
                _btnSave.Foreground = CurdTheme.ButtonAccentTextBrush ?? Brushes.White;
            }
            if (_btnCancel != null)
            {
                _btnCancel.Background = CurdTheme.ButtonCancelBgBrush;
                _btnCancel.BorderBrush = CurdTheme.ButtonCancelBorderBrush;
                _btnCancel.Foreground = CurdTheme.ButtonCancelTextBrush;
            }

            // Skeuomorphic Rocker Buttons
            if (_btnThemeDark != null && _btnThemeLight != null)
            {
                if (isDark)
                {
                    _btnThemeDark.Background = CurdTheme.ToggleActiveBgBrush;
                    _btnThemeDark.BorderBrush = CurdTheme.ToggleActiveBorderBrush;
                    _btnThemeDark.Foreground = CurdTheme.ToggleActiveTextBrush;
                    _btnThemeDark.FontWeight = FontWeights.Bold;

                    _btnThemeLight.Background = CurdTheme.ToggleInactiveBgBrush;
                    _btnThemeLight.BorderBrush = CurdTheme.ToggleInactiveBorderBrush;
                    _btnThemeLight.Foreground = CurdTheme.ToggleInactiveTextBrush;
                    _btnThemeLight.FontWeight = FontWeights.Normal;
                }
                else
                {
                    _btnThemeDark.Background = CurdTheme.ToggleInactiveBgBrush;
                    _btnThemeDark.BorderBrush = CurdTheme.ToggleInactiveBorderBrush;
                    _btnThemeDark.Foreground = CurdTheme.ToggleInactiveTextBrush;
                    _btnThemeDark.FontWeight = FontWeights.Normal;

                    _btnThemeLight.Background = CurdTheme.ToggleActiveBgBrush;
                    _btnThemeLight.BorderBrush = CurdTheme.ToggleActiveBorderBrush;
                    _btnThemeLight.Foreground = CurdTheme.ToggleActiveTextBrush;
                    _btnThemeLight.FontWeight = FontWeights.Bold;
                }
            }
        }

        private void SaveSettings()
        {
            // 1. Hotkey & Activation
            _settings.PrimaryHotkey = _cmbHotkey.SelectedIndex == 1 ? "CtrlSpace" :
                                      _cmbHotkey.SelectedIndex == 2 ? "AltD" :
                                      _cmbHotkey.SelectedIndex == 3 ? "F4" : "Backtick";
            _settings.SummonMode = _cmbSummonMode.SelectedIndex == 1 ? "TapToggle" :
                                   _cmbSummonMode.SelectedIndex == 2 ? "PushToTalk" : "Dual";
            _settings.OnlyInExplorer = _chkOnlyExplorer.IsChecked == true;
            _settings.EnableSpacePeek = _chkSpacePeek.IsChecked == true;

            // 2. Secondary Wheels
            _settings.EnableMouseWheelFlip = _chkWheelFlip.IsChecked == true;
            _settings.EnableKeyFlip = _chkKeyFlip.IsChecked == true;
            _settings.DefaultWheelLayer = _cmbDefaultLayer.SelectedIndex == 1 ? "Secondary" : "Primary";

            // 3. Quality & Output
            _settings.JpegQuality = (int)_sliderQuality.Value;
            _settings.PngOptimization = _cmbPng.SelectedIndex == 1 ? "Maximum" : "Standard";
            _settings.PdfPreset = _cmbPdf.SelectedIndex == 1 ? "HighQuality" : (_cmbPdf.SelectedIndex == 2 ? "MinimalSize" : "Standard");
            _settings.TargetDirectoryMode = _rbSubDir.IsChecked == true ? "subfolder" : "source";

            // 4. Media
            _settings.AudioBitrate = _cmbAudioBitrate.SelectedIndex == 0 ? 128 :
                                     _cmbAudioBitrate.SelectedIndex == 2 ? 256 :
                                     _cmbAudioBitrate.SelectedIndex == 3 ? 320 : 192;
            _settings.GifFramerate = _cmbGifFps.SelectedIndex == 1 ? 15 :
                                     _cmbGifFps.SelectedIndex == 2 ? 24 : 10;

            // 5. Appearance & System
            _isSaved = true;
            if (string.IsNullOrEmpty(_settings.ThemeMode))
            {
                _settings.ThemeMode = CurdTheme.CurrentMode;
            }
            _settings.ThemeAccent = GetSelectedAccent();
            _settings.WheelScale = _cmbScale.SelectedIndex == 1 ? "Compact" :
                                   _cmbScale.SelectedIndex == 2 ? "Large" : "Standard";

            _settings.StartWithWindows = _chkStartup.IsChecked == true;
            _settings.ShowNotifications = _chkNotifications.IsChecked == true;
            _settings.PlaySoundOnComplete = _chkSound.IsChecked == true;

            _settings.Save();

            if (OnSettingsSaved != null)
            {
                OnSettingsSaved(_settings);
            }

            Close();
        }
    }
}
