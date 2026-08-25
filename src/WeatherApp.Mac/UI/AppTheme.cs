using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace WeatherApp.UI
{
    /// <summary>
    /// Colours, typefaces and drawing helpers for the Avalonia head.
    ///
    /// Deliberately mirrors the WinForms Theme one member at a time. Keeping the
    /// two palettes and helper signatures aligned is what let the panel drawing
    /// code port across almost unchanged: the maths is identical and only the
    /// drawing calls differ.
    /// </summary>
    public static class AppTheme
    {
        // ---- palette (identical values to the WinForms head) -------------------
        public static readonly Color Background = Color.FromRgb(0x12, 0x16, 0x1C);
        public static readonly Color Surface = Color.FromRgb(0x1B, 0x22, 0x2B);
        public static readonly Color SurfaceAlt = Color.FromRgb(0x23, 0x2C, 0x37);
        public static readonly Color SurfaceHover = Color.FromRgb(0x2C, 0x37, 0x45);
        public static readonly Color BorderColor = Color.FromRgb(0x31, 0x3D, 0x4B);

        public static readonly Color TextColor = Color.FromRgb(0xE8, 0xED, 0xF2);
        public static readonly Color TextMutedColor = Color.FromRgb(0x93, 0xA1, 0xB0);
        public static readonly Color TextFaintColor = Color.FromRgb(0x6B, 0x78, 0x87);

        public static readonly Color AccentColor = Color.FromRgb(0x4F, 0xA3, 0xE3);
        public static readonly Color RainColor = Color.FromRgb(0x45, 0x96, 0xE0);
        public static readonly Color SnowColor = Color.FromRgb(0x9E, 0xD3, 0xF0);
        public static readonly Color IceColor = Color.FromRgb(0xC9, 0x8B, 0xE8);
        public static readonly Color ThunderColor = Color.FromRgb(0xE8, 0xC6, 0x3A);
        public static readonly Color HailColor = Color.FromRgb(0xE8, 0x97, 0x3A);

        public static readonly Color DangerColor = Color.FromRgb(0xE0, 0x52, 0x3F);
        public static readonly Color WarningColor = Color.FromRgb(0xE8, 0x97, 0x3A);
        public static readonly Color CautionColor = Color.FromRgb(0xE8, 0xC6, 0x3A);
        public static readonly Color OkColor = Color.FromRgb(0x4F, 0xC1, 0x7A);

        // Brushes are immutable here and reused on every paint; allocating a new
        // SolidColorBrush per draw call in a Render override is pure waste.
        public static readonly IBrush BackgroundBrush = new SolidColorBrush(Background);
        public static readonly IBrush SurfaceBrush = new SolidColorBrush(Surface);
        public static readonly IBrush SurfaceAltBrush = new SolidColorBrush(SurfaceAlt);
        public static readonly IBrush Text = new SolidColorBrush(TextColor);
        public static readonly IBrush TextMuted = new SolidColorBrush(TextMutedColor);
        public static readonly IBrush TextFaint = new SolidColorBrush(TextFaintColor);
        public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
        public static readonly IBrush Rain = new SolidColorBrush(RainColor);
        public static readonly IBrush White = new SolidColorBrush(Colors.White);

        public static readonly IPen BorderPen = new Pen(new SolidColorBrush(BorderColor));

        // ---- typography --------------------------------------------------------

        /// <summary>
        /// The UI font, chosen per platform.
        ///
        /// Segoe UI simply does not exist on macOS, and Avalonia's fallback for a
        /// missing family is whatever the platform hands back, which is rarely what
        /// the layout was measured against. Naming a family that is actually
        /// present on each OS keeps the metrics predictable.
        /// </summary>
        public static readonly FontFamily UiFont = ResolveFont();

        private static FontFamily ResolveFont()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Helvetica Neue ships with every macOS release; the SF system font
                // is not reliably addressable by name from a non-native toolkit.
                return new FontFamily("Helvetica Neue");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new FontFamily("Segoe UI");
            }

            return new FontFamily("DejaVu Sans");
        }

        public static readonly Typeface Regular = new Typeface(UiFont);
        public static readonly Typeface Bold = new Typeface(UiFont, FontStyle.Normal, FontWeight.Bold);

        // Point sizes from the WinForms head, converted to the device-independent
        // pixels Avalonia measures in (1pt = 1/72in, 1 DIP = 1/96in).
        public const double SizeHuge = 61;
        public const double SizeTitle = 24;
        public const double SizeHeading = 16;
        public const double SizeBody = 13;
        public const double SizeSmall = 11;
        public const double SizeGlyph = 27;

        // ---- drawing helpers ---------------------------------------------------

        public static IBrush Brush(Color color)
        {
            return new SolidColorBrush(color);
        }

        public static IBrush Brush(Color color, byte alpha)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        /// <summary>The standard card background used throughout the app.</summary>
        public static void DrawCard(DrawingContext context, Rect bounds, IBrush fill = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            context.DrawRectangle(fill ?? SurfaceBrush, BorderPen, bounds, 6, 6);
        }

        /// <summary>
        /// Draws wrapping text inside a box, optionally vertically centred.
        /// </summary>
        public static void DrawText(DrawingContext context, string text, Typeface typeface, double size,
            IBrush brush, Rect bounds, TextAlignment alignment = TextAlignment.Left, bool middle = false)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 1 || bounds.Height <= 1) return;

            FormattedText formatted = Build(text, typeface, size, brush, bounds.Width, bounds.Height, alignment);
            double y = middle ? bounds.Y + (bounds.Height - formatted.Height) / 2 : bounds.Y;
            context.DrawText(formatted, new Point(bounds.X, y));
        }

        /// <summary>
        /// Draws a single line, ellipsised if it will not fit.
        ///
        /// Avalonia's FormattedText wraps whenever MaxTextWidth is set, so a single
        /// line is expressed by capping the height to one line and letting the
        /// trimming mode handle the overflow.
        /// </summary>
        public static void DrawLineText(DrawingContext context, string text, Typeface typeface, double size,
            IBrush brush, Rect bounds, TextAlignment alignment = TextAlignment.Left, bool middle = true)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 1 || bounds.Height <= 1) return;

            double lineHeight = size * 1.45;
            FormattedText formatted = Build(text, typeface, size, brush, bounds.Width, lineHeight, alignment);

            double y = middle ? bounds.Y + (bounds.Height - formatted.Height) / 2 : bounds.Y;
            context.DrawText(formatted, new Point(bounds.X, y));
        }

        private static FormattedText Build(string text, Typeface typeface, double size, IBrush brush,
            double maxWidth, double maxHeight, TextAlignment alignment)
        {
            var formatted = new FormattedText(
                text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);

            formatted.MaxTextWidth = Math.Max(1, maxWidth);
            formatted.MaxTextHeight = Math.Max(1, maxHeight);
            formatted.TextAlignment = alignment;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            return formatted;
        }

        /// <summary>Measures a single line, for layouts that need the width up front.</summary>
        public static Size MeasureText(string text, Typeface typeface, double size)
        {
            if (string.IsNullOrEmpty(text)) return new Size(0, 0);

            var formatted = new FormattedText(
                text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Text);
            return new Size(formatted.Width, formatted.Height);
        }

        // ---- semantic colours --------------------------------------------------

        public static Color AlertColor(string severity, bool isWarning)
        {
            switch ((severity ?? string.Empty).ToLowerInvariant())
            {
                case "extreme": return DangerColor;
                case "severe": return isWarning ? DangerColor : WarningColor;
                case "moderate": return WarningColor;
                case "minor": return CautionColor;
                default: return AccentColor;
            }
        }

        /// <summary>
        /// Wind-speed shading for the jet stream map, stepped at the thresholds
        /// upper-air charts are contoured on: 70, 100, 130 and 150 knots.
        /// </summary>
        public static Color JetSpeedColor(double knots)
        {
            if (knots >= 150) return Color.FromRgb(0xF2, 0x4B, 0x8A);
            if (knots >= 130) return Color.FromRgb(0xE0, 0x52, 0x3F);
            if (knots >= 110) return Color.FromRgb(0xE8, 0x97, 0x3A);
            if (knots >= 90) return Color.FromRgb(0xE8, 0xC6, 0x3A);
            if (knots >= 70) return Color.FromRgb(0x7A, 0xD1, 0x6E);
            if (knots >= 50) return Color.FromRgb(0x45, 0x96, 0xE0);
            return Color.FromRgb(0x2E, 0x5E, 0x85);
        }
    }
}
