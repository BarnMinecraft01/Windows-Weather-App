using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Precipitation broken out by type: rain, snow, freezing rain, thunderstorms
    /// and hail, day by day.
    ///
    /// This is the tab the app exists for. A single "60% chance of precipitation"
    /// does not tell you whether to expect a shower or four inches of snow, and the
    /// hail column comes from the SPC convective outlook, which no consumer weather
    /// app surfaces at all. Where a number is genuinely not issued -- SPC hail
    /// beyond Day 3, for instance -- the cell shows a dash rather than a zero, so
    /// "not forecast" never reads as "will not happen".
    /// </summary>
    public sealed class PrecipitationPanel : WeatherPanel
    {
        private const int Gutter = 14;
        private const int HeaderRowHeight = 26;
        private const int RowHeight = 38;
        private const int NotesHeight = 60;

        private sealed class Column
        {
            public string Title;
            public int Width;
            public Func<PrecipitationDay, string> Value;
            public Func<PrecipitationDay, Color> Tint;
        }

        private readonly List<Column> _columns;

        public PrecipitationPanel()
        {
            _columns = new List<Column>
            {
                new Column
                {
                    Title = "DAY", Width = 116,
                    Value = d => d.DayLabel,
                    Tint = d => Theme.Text
                },
                new Column
                {
                    Title = "ANY PRECIP", Width = 96,
                    Value = d => Percent(d.AnyPrecipitationChance),
                    Tint = d => Shade(d.AnyPrecipitationChance, Theme.Accent)
                },
                new Column
                {
                    Title = "RAIN", Width = 78,
                    Value = d => Percent(d.RainChance),
                    Tint = d => Shade(d.RainChance, Theme.Rain)
                },
                new Column
                {
                    Title = "SNOW", Width = 78,
                    Value = d => Percent(d.SnowChance),
                    Tint = d => Shade(d.SnowChance, Theme.Snow)
                },
                new Column
                {
                    Title = "FRZ RAIN", Width = 84,
                    Value = d => Percent(d.IcyChance),
                    Tint = d => Shade(d.IcyChance, Theme.Ice)
                },
                new Column
                {
                    Title = "T-STORM", Width = 84,
                    Value = d => Percent(d.ThunderstormChance),
                    Tint = d => Shade(d.ThunderstormChance, Theme.Thunder)
                },
                new Column
                {
                    Title = "HAIL (SPC)", Width = 92,
                    Value = d => Percent(d.SevereHailChance),
                    Tint = d => Shade(d.SevereHailChance, Theme.Hail)
                },
                new Column
                {
                    Title = "SEVERE RISK", Width = 132,
                    Value = d => string.IsNullOrEmpty(d.SevereRiskCategory) ? "--" : d.SevereRiskCategory,
                    Tint = d => RiskColor(d.SevereRiskCategory)
                },
                new Column
                {
                    Title = "AMOUNT", Width = 104,
                    Value = FormatAmount,
                    Tint = d => Theme.TextMuted
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);
            g.Clear(Theme.Background);

            if (Snapshot == null || Snapshot.Precipitation.Days.Count == 0)
            {
                Theme.DrawText(g, "No precipitation outlook available.", Theme.FontBody, Theme.TextMuted,
                    ClientRectangle, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            var table = new Rectangle(
                Gutter, Gutter,
                ClientSize.Width - Gutter * 2,
                Math.Max(80, ClientSize.Height - NotesHeight - Gutter * 3));

            Theme.DrawCard(g, table);
            DrawTable(g, table);
            DrawNotes(g);
        }

        private void DrawTable(Graphics g, Rectangle bounds)
        {
            var inner = Rectangle.Inflate(bounds, -12, -8);

            // Distribute any spare width across the flexible columns so the table
            // fills the window instead of leaving a gap on the right.
            int fixedWidth = _columns.Sum(c => c.Width);
            int extra = Math.Max(0, inner.Width - fixedWidth);
            int perColumn = _columns.Count > 0 ? extra / _columns.Count : 0;

            // Header
            int x = inner.X;
            foreach (Column column in _columns)
            {
                int width = column.Width + perColumn;
                Theme.DrawText(g, column.Title, Theme.FontSmallBold, Theme.TextFaint,
                    new Rectangle(x, inner.Y, width - 8, HeaderRowHeight),
                    StringAlignment.Near, StringAlignment.Center, false);
                x += width;
            }

            using (var pen = new Pen(Theme.Border))
            {
                int lineY = inner.Y + HeaderRowHeight;
                g.DrawLine(pen, inner.X, lineY, inner.Right, lineY);
            }

            // Rows
            List<PrecipitationDay> days = Snapshot.Precipitation.Days;
            int y = inner.Y + HeaderRowHeight + 2;

            for (int i = 0; i < days.Count; i++)
            {
                if (y + RowHeight > inner.Bottom) break;

                PrecipitationDay day = days[i];

                if (i % 2 == 1)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(40, Theme.SurfaceAlt)))
                    {
                        g.FillRectangle(brush, inner.X, y, inner.Width, RowHeight);
                    }
                }

                x = inner.X;
                for (int c = 0; c < _columns.Count; c++)
                {
                    Column column = _columns[c];
                    int width = column.Width + perColumn;
                    var cell = new Rectangle(x, y, width - 8, RowHeight);

                    string text = column.Value(day);
                    Color color = column.Tint(day);

                    // The percentage columns get a filled pill whose opacity tracks
                    // the value, which makes the high-risk cells findable at a glance.
                    if (c >= 1 && c <= 6 && text != "--")
                    {
                        DrawProbabilityPill(g, cell, text, color);
                    }
                    else
                    {
                        Font font = c == 0 ? Theme.FontBodyBold : Theme.FontBody;
                        Theme.DrawText(g, text, font, color, cell,
                            StringAlignment.Near, StringAlignment.Center, false);
                    }

                    x += width;
                }

                y += RowHeight;
            }
        }

        private static void DrawProbabilityPill(Graphics g, Rectangle cell, string text, Color color)
        {
            var pill = new Rectangle(cell.X, cell.Y + cell.Height / 2 - 11, Math.Min(60, cell.Width), 22);

            using (GraphicsPath path = Theme.RoundedRectangle(pill, 11))
            using (var brush = new SolidBrush(Color.FromArgb(46, color)))
            using (var pen = new Pen(Color.FromArgb(120, color)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            Theme.DrawText(g, text, Theme.FontBodyBold, color, pill,
                StringAlignment.Center, StringAlignment.Center, false);
        }

        private void DrawNotes(Graphics g)
        {
            var bounds = new Rectangle(
                Gutter,
                ClientSize.Height - NotesHeight - Gutter,
                ClientSize.Width - Gutter * 2,
                NotesHeight);

            if (bounds.Y < Gutter) return;

            var notes = new List<string>(Snapshot.Precipitation.Notes);

            // Surface CAPE only when it is high enough to mean something; below about
            // 500 J/kg it is not a useful storm signal and would just be noise.
            PrecipitationDay peak = Snapshot.Precipitation.Days
                .Where(d => d.PeakCapeJoules.HasValue)
                .OrderByDescending(d => d.PeakCapeJoules.Value)
                .FirstOrDefault();

            if (peak != null && peak.PeakCapeJoules.Value >= 500)
            {
                notes.Insert(0, "Peak instability (CAPE) of "
                    + Math.Round(peak.PeakCapeJoules.Value).ToString("0") + " J/kg on " + peak.DayLabel
                    + " -- the higher this runs, the stronger any storms that do form.");
            }

            Theme.DrawText(g, string.Join("  ", notes.ToArray()), Theme.FontSmall, Theme.TextFaint, bounds);
        }

        // ---- formatting ------------------------------------------------------

        private static string Percent(double? value)
        {
            if (!value.HasValue) return "--";
            if (value.Value < 1) return "<1%";
            return Math.Round(value.Value).ToString("0") + "%";
        }

        /// <summary>Dims a column's colour when the probability is low.</summary>
        private static Color Shade(double? value, Color baseColor)
        {
            if (!value.HasValue) return Theme.TextFaint;
            if (value.Value < 10) return Color.FromArgb(150, baseColor);
            return baseColor;
        }

        private static Color RiskColor(string category)
        {
            switch ((category ?? string.Empty).ToLowerInvariant())
            {
                case "high": return Theme.Danger;
                case "moderate": return Theme.Danger;
                case "enhanced": return Theme.Warning;
                case "slight": return Theme.Caution;
                case "marginal": return Theme.Ok;
                case "general thunderstorms": return Theme.TextMuted;
                default: return Theme.TextFaint;
            }
        }

        private static string FormatAmount(PrecipitationDay day)
        {
            bool hasSnow = day.ExpectedSnowfallInches.HasValue && day.ExpectedSnowfallInches.Value >= 0.05;
            bool hasRain = day.ExpectedPrecipitationInches.HasValue
                           && day.ExpectedPrecipitationInches.Value >= 0.005;

            if (hasSnow && hasRain)
            {
                return Units.FormatPrecipitation(day.ExpectedPrecipitationInches)
                       + " / " + Units.FormatSnow(day.ExpectedSnowfallInches) + " snow";
            }
            if (hasSnow) return Units.FormatSnow(day.ExpectedSnowfallInches) + " snow";
            if (hasRain) return Units.FormatPrecipitation(day.ExpectedPrecipitationInches);

            return "--";
        }
    }
}
