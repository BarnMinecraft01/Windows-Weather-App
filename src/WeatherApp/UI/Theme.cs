using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WeatherApp.UI
{
    /// <summary>
    /// Colours, fonts and small drawing helpers.
    ///
    /// The palette is dark because the app is meant to sit open on a second screen
    /// during bad weather. Everything is drawn with GDI+ rather than themed common
    /// controls so that the window looks the same on Windows 7 as it does on 11 --
    /// the visual styles differ enormously across that range, and a weather display
    /// that reflows depending on the host OS is harder to read at a glance.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0x12, 0x16, 0x1C);
        public static readonly Color Surface = Color.FromArgb(0x1B, 0x22, 0x2B);
        public static readonly Color SurfaceAlt = Color.FromArgb(0x23, 0x2C, 0x37);
        public static readonly Color SurfaceHover = Color.FromArgb(0x2C, 0x37, 0x45);
        public static readonly Color Border = Color.FromArgb(0x31, 0x3D, 0x4B);

        public static readonly Color Text = Color.FromArgb(0xE8, 0xED, 0xF2);
        public static readonly Color TextMuted = Color.FromArgb(0x93, 0xA1, 0xB0);
        public static readonly Color TextFaint = Color.FromArgb(0x6B, 0x78, 0x87);

        public static readonly Color Accent = Color.FromArgb(0x4F, 0xA3, 0xE3);
        public static readonly Color AccentDim = Color.FromArgb(0x2E, 0x5E, 0x85);

        public static readonly Color Rain = Color.FromArgb(0x45, 0x96, 0xE0);
        public static readonly Color Snow = Color.FromArgb(0x9E, 0xD3, 0xF0);
        public static readonly Color Ice = Color.FromArgb(0xC9, 0x8B, 0xE8);
        public static readonly Color Thunder = Color.FromArgb(0xE8, 0xC6, 0x3A);
        public static readonly Color Hail = Color.FromArgb(0xE8, 0x97, 0x3A);

        public static readonly Color Danger = Color.FromArgb(0xE0, 0x52, 0x3F);
        public static readonly Color Warning = Color.FromArgb(0xE8, 0x97, 0x3A);
        public static readonly Color Caution = Color.FromArgb(0xE8, 0xC6, 0x3A);
        public static readonly Color Ok = Color.FromArgb(0x4F, 0xC1, 0x7A);

        private const string PreferredFamily = "Segoe UI";

        /// <summary>
        /// Builds a font, falling back if the family is unavailable. Segoe UI ships
        /// with every supported Windows version, but a stripped install can be
        /// missing it and a missing font must not crash the window.
        /// </summary>
        public static Font GetFont(float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                return new Font(PreferredFamily, size, style, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
        }

        public static readonly Font FontHuge = GetFont(46f, FontStyle.Regular);
        public static readonly Font FontTitle = GetFont(18f, FontStyle.Regular);
        public static readonly Font FontHeading = GetFont(12f, FontStyle.Bold);
        public static readonly Font FontBody = GetFont(9.75f);
        public static readonly Font FontBodyBold = GetFont(9.75f, FontStyle.Bold);
        public static readonly Font FontSmall = GetFont(8.25f);
        public static readonly Font FontSmallBold = GetFont(8.25f, FontStyle.Bold);
        public static readonly Font FontGlyph = GetFont(20f);

        /// <summary>Turns on antialiasing and high-quality text for a panel's paint pass.</summary>
        public static void PrepareGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        /// <summary>Draws the standard card background used throughout the app.</summary>
        public static void DrawCard(Graphics g, Rectangle bounds, Color? fill = null, Color? border = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (GraphicsPath path = RoundedRectangle(bounds, 6))
            using (var brush = new SolidBrush(fill ?? Surface))
            using (var pen = new Pen(border ?? Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
            StringAlignment horizontal = StringAlignment.Near,
            StringAlignment vertical = StringAlignment.Near,
            bool wrap = true)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat())
            {
                format.Alignment = horizontal;
                format.LineAlignment = vertical;
                format.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrap) format.FormatFlags |= StringFormatFlags.NoWrap;

                g.DrawString(text, font, brush, bounds, format);
            }
        }

        /// <summary>Colour for an NWS alert, keyed off its severity band.</summary>
        public static Color AlertColor(string severity, bool isWarning)
        {
            switch ((severity ?? string.Empty).ToLowerInvariant())
            {
                case "extreme": return Danger;
                case "severe": return isWarning ? Danger : Warning;
                case "moderate": return Warning;
                case "minor": return Caution;
                default: return Accent;
            }
        }

        /// <summary>
        /// Wind-speed shading for the jet stream map, stepped at the thresholds
        /// upper-air charts are contoured on: 70, 100, 130 and 150 knots.
        /// </summary>
        public static Color JetSpeedColor(double knots)
        {
            if (knots >= 150) return Color.FromArgb(0xF2, 0x4B, 0x8A);
            if (knots >= 130) return Color.FromArgb(0xE0, 0x52, 0x3F);
            if (knots >= 110) return Color.FromArgb(0xE8, 0x97, 0x3A);
            if (knots >= 90) return Color.FromArgb(0xE8, 0xC6, 0x3A);
            if (knots >= 70) return Color.FromArgb(0x7A, 0xD1, 0x6E);
            if (knots >= 50) return Color.FromArgb(0x45, 0x96, 0xE0);
            return Color.FromArgb(0x2E, 0x5E, 0x85);
        }

        /// <summary>Applies the dark palette to a stock control.</summary>
        public static void Style(Control control)
        {
            control.BackColor = Surface;
            control.ForeColor = Text;
            control.Font = FontBody;
        }

        public static Button CreateButton(string text, int width = 90)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = 27,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceAlt,
                ForeColor = Text,
                Font = FontBody,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = AccentDim;
            return button;
        }
    }
}
