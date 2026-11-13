using System;
using System.Windows;
using System.Windows.Media;

namespace LocalConvertNative.UI
{
    public static class CurdTheme
    {
        public static string CurrentMode { get; private set; }
        public static string CurrentAccent { get; private set; }
        public static bool IsDark
        {
            get { return CurrentMode != null && CurrentMode.Equals("Dark", StringComparison.OrdinalIgnoreCase); }
        }
        public static bool IsHoverTextWhite { get; private set; }

        #region Dial & Wheel Skeuomorphic Palette
        // Outer Chassis & Machined Bezel
        public static Brush ChassisBezelBrush;
        public static Pen ChassisOuterRimPen;
        public static Pen ChassisInnerRimPen;
        public static Pen ChassisKnurlPen;

        // Wedge Tactile Plate Colors & Brushes
        public static Color WedgeDefaultTopColor;
        public static Color WedgeDefaultBottomColor;
        public static Brush WedgeDefaultBgBrush;
        public static Pen WedgeDefaultPen;
        public static Pen WedgeBevelTopPen;
        public static Pen WedgeBevelBottomPen;

        // Wedge Hover / Active Illuminated State
        public static Color WedgeHoverTopColor;
        public static Color WedgeHoverBottomColor;
        public static Color WedgeHoverBorderColor;
        public static Color WedgeIndicatorColor;
        public static Brush WedgeHoverBgBrush;
        public static Pen WedgeHoverPen;
        public static Pen WedgeIndicatorPen;
        public static Pen WedgeHoverGlowPen;
        public static Brush WedgeHoverTextBrush;
        public static Brush WedgeHoverSubTextBrush;

        // Sub-Arcs
        public static Brush SubArcDefaultBgBrush;
        public static Pen SubArcDefaultPen;

        // Center Hub Physical Dial Cap
        public static Brush HubBezelBrush;
        public static Brush HubFaceBrush;
        public static Brush HubGlassGlareBrush;
        public static Brush HubCancelBgBrush;
        public static Brush HubActionBgBrush;
        public static Brush HubTitleBrush;
        public static Pen HubBorderPen;
        public static Pen HubInnerGroovePen;
        public static Pen HubCancelPen;
        public static Pen HubActionPen;

        // Dial Typography & Deboss
        public static Brush TextDarkBrush;
        public static Brush TextMutedBrush;
        public static Brush TextWhiteBrush;
        public static Brush TextLavenderBrush;
        public static Brush TextDebossShadowBrush;
        public static Brush TextRedBrush;
        #endregion

        #region Settings Window Skeuomorphic Console Palette
        public static Brush WindowBgBrush;
        public static Brush WindowBorderBrush;
        public static Pen WindowHighlightPen;

        public static Brush HeaderBgBrush;
        public static Brush HeaderBorderBrush;
        public static Brush HeaderTitleBrush;
        public static Brush HeaderSubTitleBrush;

        public static Brush CardWellBgBrush;
        public static Brush CardWellBorderBrush;
        public static Brush CardWellHighlightBrush;
        public static Brush CardWellShadowBrush;

        public static Brush FieldBgBrush;
        public static Brush FieldBorderBrush;
        public static Brush FieldTextBrush;
        public static Brush FieldLabelBrush;
        public static Brush FieldHeaderBrush;

        public static Brush ButtonAccentBgBrush;
        public static Brush ButtonAccentBorderBrush;
        public static Brush ButtonAccentTextBrush;
        public static Brush ButtonCancelBgBrush;
        public static Brush ButtonCancelBorderBrush;
        public static Brush ButtonCancelTextBrush;

        public static Brush SliderTrackBgBrush;
        public static Brush SliderTrackFillBrush;
        public static Brush SliderThumbBgBrush;
        public static Brush SliderThumbBorderBrush;

        public static Brush ToggleActiveBgBrush;
        public static Brush ToggleActiveBorderBrush;
        public static Brush ToggleActiveTextBrush;
        public static Brush ToggleInactiveBgBrush;
        public static Brush ToggleInactiveBorderBrush;
        public static Brush ToggleInactiveTextBrush;
        #endregion

        static CurdTheme()
        {
            SetTheme("Purple", "Dark");
        }

        public static void SetTheme(string themeAccent, string themeMode = null)
        {
            if (string.IsNullOrEmpty(themeAccent)) themeAccent = "Purple";
            if (!string.IsNullOrEmpty(themeMode)) CurrentMode = themeMode;
            if (string.IsNullOrEmpty(CurrentMode)) CurrentMode = "Dark";
            CurrentAccent = themeAccent;

            bool isDark = IsDark;

            // 1. Accent Color Setup
            Color accTop, accBottom, accBorder, accGlow, accSoft;
            Color contrastTextColor = Colors.White;
            Color contrastSubTextColor = Colors.White;
            bool hoverTextWhite = true;

            if (themeAccent.Equals("DarkSlate", StringComparison.OrdinalIgnoreCase))
            {
                accTop = Color.FromRgb(0x47, 0x55, 0x69);
                accBottom = Color.FromRgb(0x1E, 0x29, 0x3B);
                accBorder = Color.FromRgb(0x0F, 0x17, 0x2A);
                accGlow = Color.FromRgb(0x94, 0xA3, 0xB8);
                accSoft = Color.FromRgb(0x33, 0x41, 0x55);
                hoverTextWhite = true;
            }
            else if (themeAccent.Equals("Emerald", StringComparison.OrdinalIgnoreCase))
            {
                accTop = Color.FromRgb(0x10, 0xB9, 0x81);
                accBottom = Color.FromRgb(0x04, 0x78, 0x57);
                accBorder = Color.FromRgb(0x06, 0x5F, 0x46);
                accGlow = Color.FromRgb(0x6E, 0xE7, 0xB7);
                accSoft = Color.FromRgb(0x05, 0x96, 0x69);
                hoverTextWhite = true;
            }
            else if (themeAccent.Equals("Cobalt", StringComparison.OrdinalIgnoreCase))
            {
                accTop = Color.FromRgb(0x3B, 0x82, 0xF6);
                accBottom = Color.FromRgb(0x1D, 0x4E, 0xD8);
                accBorder = Color.FromRgb(0x1E, 0x40, 0xAF);
                accGlow = Color.FromRgb(0x93, 0xC5, 0xFD);
                accSoft = Color.FromRgb(0x25, 0x63, 0xEB);
                hoverTextWhite = true;
            }
            else if (themeAccent.Equals("Amber", StringComparison.OrdinalIgnoreCase))
            {
                accTop = Color.FromRgb(0xF5, 0x9E, 0x0B);
                accBottom = Color.FromRgb(0xB4, 0x53, 0x09);
                accBorder = Color.FromRgb(0x92, 0x40, 0x0E);
                accGlow = Color.FromRgb(0xFC, 0xD3, 0x4D);
                accSoft = Color.FromRgb(0xD9, 0x77, 0x06);
                hoverTextWhite = true;
            }
            else if (themeAccent.Equals("PastelLavender", StringComparison.OrdinalIgnoreCase))
            {
                // Soft pastel lilac/lavender – WCAG AAA 8.66:1
                accTop = Color.FromRgb(0xDD, 0xD6, 0xFE);
                accBottom = Color.FromRgb(0xC4, 0xB5, 0xFD);
                accBorder = Color.FromRgb(0x8B, 0x5C, 0xF6);
                accGlow = Color.FromRgb(0xF5, 0xF3, 0xFF);
                accSoft = Color.FromRgb(0xA7, 0x8B, 0xFA);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x1E, 0x1B, 0x4B); // Deep indigo
                contrastSubTextColor = Color.FromRgb(0x43, 0x38, 0xCA);
            }
            else if (themeAccent.Equals("PastelMint", StringComparison.OrdinalIgnoreCase))
            {
                // Soft pastel spearmint/sage – WCAG AAA 9.94:1
                accTop = Color.FromRgb(0xA7, 0xF3, 0xD0);
                accBottom = Color.FromRgb(0x6E, 0xE7, 0xB7);
                accBorder = Color.FromRgb(0x05, 0x96, 0x69);
                accGlow = Color.FromRgb(0xEC, 0xFD, 0xF5);
                accSoft = Color.FromRgb(0x34, 0xD3, 0x99);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x02, 0x2C, 0x22); // Deep forest obsidian
                contrastSubTextColor = Color.FromRgb(0x06, 0x5F, 0x46);
            }
            else if (themeAccent.Equals("PastelRose", StringComparison.OrdinalIgnoreCase))
            {
                // Soft pastel blush/rose – WCAG AAA 8.27:1
                accTop = Color.FromRgb(0xFE, 0xCD, 0xD3);
                accBottom = Color.FromRgb(0xFD, 0xA4, 0xAF);
                accBorder = Color.FromRgb(0xE1, 0x1D, 0x48);
                accGlow = Color.FromRgb(0xFF, 0xF1, 0xF2);
                accSoft = Color.FromRgb(0xFB, 0x71, 0x85);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x4C, 0x05, 0x19); // Deepest wine
                contrastSubTextColor = Color.FromRgb(0x9F, 0x12, 0x39);
            }
            else if (themeAccent.Equals("PastelSky", StringComparison.OrdinalIgnoreCase))
            {
                // Soft pastel powder blue – WCAG AAA 8.32:1
                accTop = Color.FromRgb(0xBA, 0xE6, 0xFD);
                accBottom = Color.FromRgb(0x7D, 0xD3, 0xFC);
                accBorder = Color.FromRgb(0x02, 0x84, 0xC7);
                accGlow = Color.FromRgb(0xF0, 0xF9, 0xFF);
                accSoft = Color.FromRgb(0x38, 0xBD, 0xF8);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x08, 0x2F, 0x49); // Deep midnight navy
                contrastSubTextColor = Color.FromRgb(0x03, 0x69, 0xA1);
            }
            else if (themeAccent.Equals("PastelPeach", StringComparison.OrdinalIgnoreCase))
            {
                // Soft pastel apricot/sherbet – WCAG AAA 9.28:1
                accTop = Color.FromRgb(0xFE, 0xD7, 0xAA);
                accBottom = Color.FromRgb(0xFD, 0xBA, 0x74);
                accBorder = Color.FromRgb(0xEA, 0x58, 0x0C);
                accGlow = Color.FromRgb(0xFF, 0xF7, 0xED);
                accSoft = Color.FromRgb(0xFB, 0x92, 0x3C);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x43, 0x14, 0x07); // Deep espresso rust
                contrastSubTextColor = Color.FromRgb(0x9A, 0x34, 0x12);
            }
            else if (themeAccent.Equals("PastelMatcha", StringComparison.OrdinalIgnoreCase))
            {
                // Soft pastel tea celadon – WCAG AAA 7.80:1
                accTop = Color.FromRgb(0xEC, 0xFC, 0xCB);
                accBottom = Color.FromRgb(0xD9, 0xF9, 0x9D);
                accBorder = Color.FromRgb(0x65, 0xA3, 0x0D);
                accGlow = Color.FromRgb(0xF7, 0xFE, 0xE7);
                accSoft = Color.FromRgb(0xA3, 0xE6, 0x35);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x14, 0x53, 0x2D); // Deep forest olive
                contrastSubTextColor = Color.FromRgb(0x16, 0x65, 0x34);
            }
            else if (themeAccent.Equals("NordicFrost", StringComparison.OrdinalIgnoreCase))
            {
                // Minimalist clean alpine slate – WCAG AAA 14.48:1
                accTop = Color.FromRgb(0xF1, 0xF5, 0xF9);
                accBottom = Color.FromRgb(0xE2, 0xE8, 0xF0);
                accBorder = Color.FromRgb(0x64, 0x74, 0x8B);
                accGlow = Color.FromRgb(0xFF, 0xFF, 0xFF);
                accSoft = Color.FromRgb(0xCB, 0xD5, 0xE1);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x0F, 0x17, 0x2A); // Midnight obsidian
                contrastSubTextColor = Color.FromRgb(0x33, 0x41, 0x55);
            }
            else if (themeAccent.Equals("Monochrome", StringComparison.OrdinalIgnoreCase))
            {
                // Minimalist clean graphite slate – WCAG AAA 10.35:1
                accTop = Color.FromRgb(0x64, 0x74, 0x8B);
                accBottom = Color.FromRgb(0x33, 0x41, 0x55);
                accBorder = Color.FromRgb(0x1E, 0x29, 0x3B);
                accGlow = Color.FromRgb(0xE2, 0xE8, 0xF0);
                accSoft = Color.FromRgb(0x47, 0x55, 0x69);
                hoverTextWhite = true;
            }
            else if (themeAccent.Equals("Solar", StringComparison.OrdinalIgnoreCase))
            {
                // Clean sunburst gold – WCAG AAA 7.60:1
                accTop = Color.FromRgb(0xFD, 0xE0, 0x47);
                accBottom = Color.FromRgb(0xEA, 0xB3, 0x08);
                accBorder = Color.FromRgb(0xCA, 0x8A, 0x04);
                accGlow = Color.FromRgb(0xFE, 0xF9, 0xC3);
                accSoft = Color.FromRgb(0xFA, 0xCC, 0x15);
                hoverTextWhite = false;
                contrastTextColor = Color.FromRgb(0x42, 0x20, 0x06); // Deep chocolate umber
                contrastSubTextColor = Color.FromRgb(0x71, 0x3F, 0x12);
            }
            else if (themeAccent.Equals("Ocean", StringComparison.OrdinalIgnoreCase))
            {
                // Clean deep marine – WCAG AA 5.93:1
                accTop = Color.FromRgb(0x02, 0x84, 0xC7);
                accBottom = Color.FromRgb(0x03, 0x69, 0xA1);
                accBorder = Color.FromRgb(0x07, 0x59, 0x85);
                accGlow = Color.FromRgb(0xE0, 0xF2, 0xFE);
                accSoft = Color.FromRgb(0x0E, 0xA5, 0xE9);
                hoverTextWhite = true;
            }
            else // Purple (Default)
            {
                accTop = Color.FromRgb(0x8B, 0x5C, 0xF6);
                accBottom = Color.FromRgb(0x6D, 0x28, 0xD9);
                accBorder = Color.FromRgb(0x5B, 0x21, 0xB6);
                accGlow = Color.FromRgb(0xC4, 0xB5, 0xFD);
                accSoft = Color.FromRgb(0x7C, 0x3A, 0xED);
                hoverTextWhite = true;
            }

            IsHoverTextWhite = hoverTextWhite;
            if (hoverTextWhite)
            {
                WedgeHoverTextBrush = Brushes.White;
                WedgeHoverSubTextBrush = CreateFrozenSolidBrush(accGlow);
                ButtonAccentTextBrush = Brushes.White;
            }
            else
            {
                WedgeHoverTextBrush = CreateFrozenSolidBrush(contrastTextColor);
                WedgeHoverSubTextBrush = CreateFrozenSolidBrush(contrastSubTextColor);
                ButtonAccentTextBrush = CreateFrozenSolidBrush(contrastTextColor);
            }

            WedgeHoverTopColor = accTop;
            WedgeHoverBottomColor = accBottom;
            WedgeHoverBorderColor = accBorder;
            WedgeIndicatorColor = accGlow;

            WedgeHoverBgBrush = CreateVerticalGradient(accTop, accBottom);
            WedgeHoverPen = CreatePen(accBorder, 2.0);
            WedgeIndicatorPen = CreatePen(accGlow, 3.5);
            WedgeHoverGlowPen = CreatePen(accGlow, 1.5, 180);

            if (isDark)
            {
                HubTitleBrush = CreateFrozenSolidBrush(accGlow);
            }
            else
            {
                HubTitleBrush = CreateFrozenSolidBrush(accBorder);
            }

            // 2. Dark vs Light Skeuomorphic Dial & Wheel
            if (isDark)
            {
                // Unhovered Wedges: Deep gunmetal / obsidian plate with metallic sheen
                WedgeDefaultTopColor = Color.FromRgb(0x28, 0x26, 0x3A);
                WedgeDefaultBottomColor = Color.FromRgb(0x18, 0x17, 0x24);
                WedgeDefaultBgBrush = CreateVerticalGradient(WedgeDefaultTopColor, WedgeDefaultBottomColor);
                WedgeDefaultPen = CreatePen(Color.FromRgb(0x3E, 0x3B, 0x54), 1.2);
                WedgeBevelTopPen = CreatePen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 1.2); // Specular highlight
                WedgeBevelBottomPen = CreatePen(Color.FromArgb(0x80, 0x0A, 0x09, 0x12), 1.5); // Deep seam groove

                // Outer Chassis Bezel
                ChassisBezelBrush = CreateVerticalGradient(Color.FromRgb(0x35, 0x32, 0x4A), Color.FromRgb(0x12, 0x11, 0x1B));
                ChassisOuterRimPen = CreatePen(Color.FromRgb(0x4D, 0x49, 0x68), 1.8);
                ChassisInnerRimPen = CreatePen(Color.FromArgb(0x60, 0x0B, 0x0A, 0x12), 2.0);
                ChassisKnurlPen = CreatePen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF), 1.0);

                // Sub-Arcs
                SubArcDefaultBgBrush = CreateVerticalGradient(Color.FromRgb(0x22, 0x20, 0x32), Color.FromRgb(0x15, 0x14, 0x20));
                SubArcDefaultPen = CreatePen(Color.FromRgb(0x48, 0x43, 0x66), 1.2);

                // Center Hub Dial
                HubBezelBrush = CreateVerticalGradient(Color.FromRgb(0x44, 0x40, 0x5E), Color.FromRgb(0x1B, 0x19, 0x27));
                HubFaceBrush = CreateRadialGradient(Color.FromRgb(0x25, 0x23, 0x35), Color.FromRgb(0x13, 0x12, 0x1C));
                HubGlassGlareBrush = CreateVerticalGradient(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
                HubBorderPen = CreatePen(Color.FromRgb(0x56, 0x51, 0x75), 1.8);
                HubInnerGroovePen = CreatePen(Color.FromArgb(0x90, 0x09, 0x08, 0x10), 1.5);

                HubCancelBgBrush = CreateRadialGradient(Color.FromRgb(0x45, 0x18, 0x20), Color.FromRgb(0x23, 0x0A, 0x0E));
                HubCancelPen = CreatePen(Color.FromRgb(0xF4, 0x3F, 0x5E), 2.0);
                HubActionBgBrush = CreateRadialGradient(Color.FromRgb(0x2C, 0x29, 0x40), Color.FromRgb(0x16, 0x14, 0x22));
                HubActionPen = CreatePen(accGlow, 2.0);

                // Typography - WCAG AAA Ultra-High Contrast for Dark Mode
                TextDarkBrush = CreateFrozenSolidBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));       // Pure crisp white
                TextMutedBrush = CreateFrozenSolidBrush(Color.FromRgb(0xEA, 0xE7, 0xF4));      // Luminous silver
                TextWhiteBrush = Brushes.White;
                TextLavenderBrush = CreateFrozenSolidBrush(accGlow);                           // Radiant accent glow
                TextDebossShadowBrush = null;                                                  // Zero smudge in dark mode
                TextRedBrush = CreateFrozenSolidBrush(Color.FromRgb(0xFB, 0x71, 0x85));

                // Settings Window Console (Dark Skeuomorphic Machine)
                WindowBgBrush = CreateVerticalGradient(Color.FromRgb(0x1A, 0x18, 0x26), Color.FromRgb(0x12, 0x11, 0x1C));
                WindowBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x42, 0x3E, 0x5A));
                WindowHighlightPen = CreatePen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1.2);

                HeaderBgBrush = CreateVerticalGradient(Color.FromRgb(0x26, 0x23, 0x38), Color.FromRgb(0x17, 0x15, 0x23));
                HeaderBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x3B, 0x37, 0x50));
                HeaderTitleBrush = CreateFrozenSolidBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                HeaderSubTitleBrush = CreateFrozenSolidBrush(Color.FromRgb(0xEA, 0xE7, 0xF4));

                CardWellBgBrush = CreateVerticalGradient(Color.FromRgb(0x13, 0x12, 0x1E), Color.FromRgb(0x0E, 0x0D, 0x16));
                CardWellBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x2D, 0x2A, 0x3E));
                CardWellHighlightBrush = CreateFrozenSolidBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                CardWellShadowBrush = CreateFrozenSolidBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));

                FieldBgBrush = CreateVerticalGradient(Color.FromRgb(0x1F, 0x1D, 0x2D), Color.FromRgb(0x18, 0x16, 0x24));
                FieldBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x3C, 0x38, 0x52));
                FieldTextBrush = CreateFrozenSolidBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                FieldLabelBrush = CreateFrozenSolidBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                FieldHeaderBrush = CreateFrozenSolidBrush(accGlow);

                ButtonAccentBgBrush = CreateVerticalGradient(accTop, accBottom);
                ButtonAccentBorderBrush = CreateFrozenSolidBrush(accBorder);
                ButtonCancelBgBrush = CreateVerticalGradient(Color.FromRgb(0x27, 0x25, 0x36), Color.FromRgb(0x1B, 0x19, 0x26));
                ButtonCancelBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x40, 0x3C, 0x54));
                ButtonCancelTextBrush = CreateFrozenSolidBrush(Color.FromRgb(0xD8, 0xD4, 0xE8));

                SliderTrackBgBrush = CreateFrozenSolidBrush(Color.FromRgb(0x0B, 0x0A, 0x12));
                SliderTrackFillBrush = CreateFrozenSolidBrush(accSoft);
                SliderThumbBgBrush = CreateVerticalGradient(Color.FromRgb(0xEE, 0xEA, 0xF8), Color.FromRgb(0x9E, 0x98, 0xB4));
                SliderThumbBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x18, 0x16, 0x24));

                ToggleActiveBgBrush = CreateVerticalGradient(accTop, accBottom);
                ToggleActiveBorderBrush = CreateFrozenSolidBrush(accBorder);
                ToggleActiveTextBrush = ButtonAccentTextBrush ?? Brushes.White;
                ToggleInactiveBgBrush = CreateVerticalGradient(Color.FromRgb(0x1D, 0x1B, 0x2B), Color.FromRgb(0x14, 0x13, 0x20));
                ToggleInactiveBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x35, 0x31, 0x48));
                ToggleInactiveTextBrush = CreateFrozenSolidBrush(Color.FromRgb(0x9E, 0x98, 0xB4));
            }
            else // Light Mode (Skeuomorphic Polished Aluminum / Champagne Cream)
            {
                // Unhovered Wedges: Brushed platinum / warm cream ceramic
                WedgeDefaultTopColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
                WedgeDefaultBottomColor = Color.FromRgb(0xEC, 0xE7, 0xDE);
                WedgeDefaultBgBrush = CreateVerticalGradient(WedgeDefaultTopColor, WedgeDefaultBottomColor);
                WedgeDefaultPen = CreatePen(Color.FromRgb(0xE2, 0xE8, 0xF0), 1.2);
                WedgeBevelTopPen = CreatePen(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 1.2); // Crisp top highlight
                WedgeBevelBottomPen = CreatePen(Color.FromArgb(0x40, 0x8C, 0x82, 0x73), 1.5); // Warm crevice shadow

                // Outer Chassis Bezel
                ChassisBezelBrush = CreateVerticalGradient(Color.FromRgb(0xFA, 0xF8, 0xF4), Color.FromRgb(0xD4, 0xCE, 0xC0));
                ChassisOuterRimPen = CreatePen(Color.FromRgb(0xB8, 0xB0, 0x9F), 1.8);
                ChassisInnerRimPen = CreatePen(Color.FromArgb(0x40, 0x99, 0x90, 0x80), 2.0);
                ChassisKnurlPen = CreatePen(Color.FromArgb(0x50, 0x80, 0x78, 0x68), 1.0);

                // Sub-Arcs
                SubArcDefaultBgBrush = CreateVerticalGradient(Color.FromRgb(0xF8, 0xF6, 0xF0), Color.FromRgb(0xE6, 0xE0, 0xD4));
                SubArcDefaultPen = CreatePen(Color.FromRgb(0xC6, 0xBD, 0xAC), 1.2);

                // Center Hub Dial
                HubBezelBrush = CreateVerticalGradient(Color.FromRgb(0xFF, 0xFD, 0xFA), Color.FromRgb(0xCF, 0xC8, 0xB8));
                HubFaceBrush = CreateRadialGradient(Color.FromRgb(0xFC, 0xFA, 0xF7), Color.FromRgb(0xE4, 0xDC, 0xCC));
                HubGlassGlareBrush = CreateVerticalGradient(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
                HubBorderPen = CreatePen(Color.FromRgb(0xB0, 0xA8, 0x96), 1.8);
                HubInnerGroovePen = CreatePen(Color.FromArgb(0x60, 0x90, 0x85, 0x75), 1.5);

                HubCancelBgBrush = CreateRadialGradient(Color.FromRgb(0xFF, 0xF1, 0xF2), Color.FromRgb(0xFD, 0xC5, 0xCB));
                HubCancelPen = CreatePen(Color.FromRgb(0xFB, 0x71, 0x85), 2.0);
                HubActionBgBrush = CreateRadialGradient(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xEE, 0xE9, 0xDD));
                HubActionPen = CreatePen(accBorder, 2.0);

                // Typography
                TextDarkBrush = CreateFrozenSolidBrush(Color.FromRgb(0x1E, 0x1B, 0x28));
                TextMutedBrush = CreateFrozenSolidBrush(Color.FromRgb(0x6A, 0x63, 0x58));
                TextWhiteBrush = Brushes.White;
                TextLavenderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xFD, 0xFA, 0xF5));
                TextDebossShadowBrush = CreateFrozenSolidBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)); // White bottom highlight = engraved into metal!
                TextRedBrush = CreateFrozenSolidBrush(Color.FromRgb(0xE1, 0x1D, 0x48));

                // Settings Window Console (Light Skeuomorphic Control Deck)
                WindowBgBrush = CreateVerticalGradient(Color.FromRgb(0xF5, 0xF2, 0xEB), Color.FromRgb(0xE7, 0xE2, 0xD6));
                WindowBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xC2, 0xBA, 0xAA));
                WindowHighlightPen = CreatePen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 1.5);

                HeaderBgBrush = CreateVerticalGradient(Color.FromRgb(0xEB, 0xE6, 0xDA), Color.FromRgb(0xD6, 0xD0, 0xC0));
                HeaderBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xBA, 0xB2, 0xA0));
                HeaderTitleBrush = CreateFrozenSolidBrush(Color.FromRgb(0x24, 0x20, 0x32));
                HeaderSubTitleBrush = CreateFrozenSolidBrush(Color.FromRgb(0x62, 0x5B, 0x4F));

                CardWellBgBrush = CreateVerticalGradient(Color.FromRgb(0xFC, 0xFA, 0xF5), Color.FromRgb(0xF1, 0xEC, 0xE1));
                CardWellBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xD2, 0xCB, 0xBC));
                CardWellHighlightBrush = CreateFrozenSolidBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
                CardWellShadowBrush = CreateFrozenSolidBrush(Color.FromArgb(0x30, 0x80, 0x75, 0x65));

                FieldBgBrush = CreateVerticalGradient(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF7, 0xF4, 0xED));
                FieldBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xC4, 0xBC, 0xAC));
                FieldTextBrush = CreateFrozenSolidBrush(Color.FromRgb(0x20, 0x1C, 0x2C));
                FieldLabelBrush = CreateFrozenSolidBrush(Color.FromRgb(0x56, 0x4F, 0x44));
                FieldHeaderBrush = CreateFrozenSolidBrush(accBorder);

                ButtonAccentBgBrush = CreateVerticalGradient(accTop, accBottom);
                ButtonAccentBorderBrush = CreateFrozenSolidBrush(accBorder);
                ButtonCancelBgBrush = CreateVerticalGradient(Color.FromRgb(0xFD, 0xFB, 0xF7), Color.FromRgb(0xE4, 0xDE, 0xD2));
                ButtonCancelBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xC0, 0xB8, 0xA6));
                ButtonCancelTextBrush = CreateFrozenSolidBrush(Color.FromRgb(0x38, 0x33, 0x2B));

                SliderTrackBgBrush = CreateFrozenSolidBrush(Color.FromRgb(0xD0, 0xC9, 0xB9));
                SliderTrackFillBrush = CreateFrozenSolidBrush(accSoft);
                SliderThumbBgBrush = CreateVerticalGradient(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xD6, 0xD0, 0xC2));
                SliderThumbBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0x8C, 0x82, 0x72));

                ToggleActiveBgBrush = CreateVerticalGradient(accTop, accBottom);
                ToggleActiveBorderBrush = CreateFrozenSolidBrush(accBorder);
                ToggleActiveTextBrush = ButtonAccentTextBrush ?? Brushes.White;
                ToggleInactiveBgBrush = CreateVerticalGradient(Color.FromRgb(0xF6, 0xF3, 0xEC), Color.FromRgb(0xE0, 0xDA, 0xCC));
                ToggleInactiveBorderBrush = CreateFrozenSolidBrush(Color.FromRgb(0xC6, 0xBD, 0xAC));
                ToggleInactiveTextBrush = CreateFrozenSolidBrush(Color.FromRgb(0x58, 0x51, 0x45));
            }
        }

        #region Helpers
        public static LinearGradientBrush CreateVerticalGradient(Color top, Color bottom)
        {
            var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
            brush.Freeze();
            return brush;
        }

        public static RadialGradientBrush CreateRadialGradient(Color center, Color edge)
        {
            var brush = new RadialGradientBrush(center, edge);
            brush.Freeze();
            return brush;
        }

        public static SolidColorBrush CreateFrozenSolidBrush(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        public static Pen CreatePen(Color c, double thickness, byte alpha = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
        #endregion
    }
}
