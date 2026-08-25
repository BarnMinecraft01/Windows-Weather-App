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
    /// The ten-day forecast.
    ///
    /// Each day's high and low are drawn as a bar positioned against the range of
    /// the whole period rather than printed as two numbers, so a cold snap in the
    /// middle of the week is visible as a shape instead of something you have to
    /// find by comparing figures. Selecting a day shows the forecast office's own
    /// narrative underneath, which is the part most apps dropped.
    /// </summary>
    public sealed class ForecastPanel : WeatherPanel
    {
        private const int Gutter = 14;
        private const int DetailHeight = 116;
        private const int MinRowHeight = 34;

        private int _selectedIndex;
        private int _hoverIndex = -1;

        public ForecastPanel()
        {
            Cursor = Cursors.Hand;
        }

        protected override void OnSnapshotChanged()
        {
            _selectedIndex = 0;
            _hoverIndex = -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index = IndexAt(e.Y);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            int index = IndexAt(e.Y);
            if (index >= 0 && index != _selectedIndex)
            {
                _selectedIndex = index;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);
            g.Clear(Theme.Background);

            if (Snapshot == null || Snapshot.Days.Count == 0)
            {
                Theme.DrawText(g, "No forecast available.", Theme.FontBody, Theme.TextMuted,
                    ClientRectangle, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            List<ForecastDay> days = Days();
            Rectangle listBounds = ListBounds();
            Theme.DrawCard(g, listBounds);

            int rowHeight = RowHeight(days.Count);
            double minimum, maximum;
            TemperatureRange(days, out minimum, out maximum);

            for (int i = 0; i < days.Count; i++)
            {
                var row = new Rectangle(
                    listBounds.X + 1,
                    listBounds.Y + 6 + i * rowHeight,
                    listBounds.Width - 2,
                    rowHeight);

                if (row.Bottom > listBounds.Bottom - 4) break;
                DrawRow(g, row, days[i], i, minimum, maximum);
            }

            DrawDetail(g, days);
        }

        private void DrawRow(
            Graphics g, Rectangle bounds, ForecastDay day, int index, double minimum, double maximum)
        {
            if (index == _selectedIndex)
            {
                using (var brush = new SolidBrush(Theme.SurfaceAlt))
                using (GraphicsPath path = Theme.RoundedRectangle(Rectangle.Inflate(bounds, -4, -1), 4))
                {
                    g.FillPath(brush, path);
                }
            }
            else if (index == _hoverIndex)
            {
                using (var brush = new SolidBrush(Color.FromArgb(60, Theme.SurfaceHover)))
                using (GraphicsPath path = Theme.RoundedRectangle(Rectangle.Inflate(bounds, -4, -1), 4))
                {
                    g.FillPath(brush, path);
                }
            }

            int x = bounds.X + 14;
            int centreY = bounds.Y;
            int height = bounds.Height;

            // Day name
            Theme.DrawText(g, day.DayLabel, Theme.FontBodyBold, Theme.Text,
                new Rectangle(x, centreY, 74, height), StringAlignment.Near, StringAlignment.Center, false);
            x += 74;

            Theme.DrawText(g, day.Date.ToString("MMM d"), Theme.FontSmall, Theme.TextFaint,
                new Rectangle(x, centreY, 56, height), StringAlignment.Near, StringAlignment.Center, false);
            x += 56;

            // Condition glyph and text
            Theme.DrawText(g, WeatherCodes.Glyph(day.WeatherCode, true), Theme.FontBody, Theme.Accent,
                new Rectangle(x, centreY, 24, height), StringAlignment.Center, StringAlignment.Center, false);
            x += 28;

            int summaryWidth = Math.Max(80, bounds.Width - x - 330);
            Theme.DrawText(g, day.Summary ?? "--", Theme.FontBody, Theme.Text,
                new Rectangle(x, centreY, summaryWidth, height),
                StringAlignment.Near, StringAlignment.Center, false);
            x += summaryWidth + 8;

            // Precipitation chance
            string pop = day.PrecipitationProbability.HasValue && day.PrecipitationProbability.Value > 0
                ? Units.FormatPercent(day.PrecipitationProbability)
                : "--";
            Color popColor = day.PrecipitationProbability.HasValue && day.PrecipitationProbability.Value >= 30
                ? Theme.Rain
                : Theme.TextFaint;

            Theme.DrawText(g, pop, Theme.FontBodyBold, popColor,
                new Rectangle(x, centreY, 48, height), StringAlignment.Far, StringAlignment.Center, false);
            x += 56;

            // Low / range bar / high
            Theme.DrawText(g, Units.FormatTemperature(day.LowF), Theme.FontBody, Theme.TextMuted,
                new Rectangle(x, centreY, 44, height), StringAlignment.Far, StringAlignment.Center, false);
            x += 50;

            int barWidth = Math.Max(40, bounds.Right - x - 62);
            DrawRangeBar(g, new Rectangle(x, centreY + height / 2 - 4, barWidth, 8), day, minimum, maximum);
            x += barWidth + 8;

            Theme.DrawText(g, Units.FormatTemperature(day.HighF), Theme.FontBodyBold, Theme.Text,
                new Rectangle(x, centreY, 46, height), StringAlignment.Near, StringAlignment.Center, false);
        }

        /// <summary>
        /// Draws one day's high-to-low span against the whole period's range, so the
        /// bars line up into a temperature profile down the column.
        /// </summary>
        private static void DrawRangeBar(
            Graphics g, Rectangle track, ForecastDay day, double minimum, double maximum)
        {
            using (var brush = new SolidBrush(Color.FromArgb(70, Theme.Border)))
            using (GraphicsPath path = Theme.RoundedRectangle(track, 4))
            {
                g.FillPath(brush, path);
            }

            if (!day.HighF.HasValue || !day.LowF.HasValue) return;

            double span = Math.Max(1d, maximum - minimum);
            double lowFraction = (day.LowF.Value - minimum) / span;
            double highFraction = (day.HighF.Value - minimum) / span;

            int left = track.X + (int)(track.Width * lowFraction);
            int right = track.X + (int)(track.Width * highFraction);
            if (right - left < 6) right = left + 6;
            if (right > track.Right) { right = track.Right; left = Math.Max(track.X, right - 6); }

            var fill = new Rectangle(left, track.Y, right - left, track.Height);

            // Cold end blue, warm end orange, so the gradient itself reads as temperature.
            using (var brush = new LinearGradientBrush(
                       new Rectangle(track.X, track.Y, track.Width, track.Height),
                       Color.FromArgb(0x4F, 0x9B, 0xE0), Color.FromArgb(0xE8, 0x8B, 0x3A),
                       LinearGradientMode.Horizontal))
            using (GraphicsPath path = Theme.RoundedRectangle(fill, 4))
            {
                g.FillPath(brush, path);
            }
        }

        private void DrawDetail(Graphics g, List<ForecastDay> days)
        {
            var bounds = new Rectangle(
                Gutter,
                ClientSize.Height - DetailHeight - Gutter,
                ClientSize.Width - Gutter * 2,
                DetailHeight);

            if (bounds.Width <= 20 || bounds.Y < Gutter) return;

            Theme.DrawCard(g, bounds);
            var inner = Rectangle.Inflate(bounds, -16, -12);

            int index = Math.Min(Math.Max(0, _selectedIndex), days.Count - 1);
            ForecastDay day = days[index];

            string heading = day.Date.ToString("dddd, MMMM d");
            if (!string.IsNullOrEmpty(day.Source)) heading += "   ·   " + day.Source;

            Theme.DrawText(g, heading, Theme.FontSmallBold, Theme.TextMuted,
                new Rectangle(inner.X, inner.Y, inner.Width, 16), wrap: false);

            string narrative = !string.IsNullOrWhiteSpace(day.DetailedForecast)
                ? day.DetailedForecast
                : BuildFallbackNarrative(day);

            Theme.DrawText(g, narrative, Theme.FontBody, Theme.Text,
                new Rectangle(inner.X, inner.Y + 20, inner.Width, inner.Height - 20));
        }

        /// <summary>
        /// Days past the NWS window have no narrative, so one is assembled from the
        /// model values rather than leaving the pane blank.
        /// </summary>
        private static string BuildFallbackNarrative(ForecastDay day)
        {
            var parts = new List<string>();

            parts.Add((day.Summary ?? "No summary available") + ".");

            if (day.HighF.HasValue && day.LowF.HasValue)
            {
                parts.Add("High near " + Units.FormatTemperature(day.HighF)
                          + ", low around " + Units.FormatTemperature(day.LowF) + ".");
            }

            if (day.PrecipitationProbability.HasValue && day.PrecipitationProbability.Value > 0)
            {
                parts.Add("Chance of precipitation " + Units.FormatPercent(day.PrecipitationProbability) + ".");
            }

            if (day.SnowfallInches.HasValue && day.SnowfallInches.Value > 0.05)
            {
                parts.Add("Snow accumulation around " + Units.FormatSnow(day.SnowfallInches) + ".");
            }
            else if (day.PrecipitationInches.HasValue && day.PrecipitationInches.Value > 0.005)
            {
                parts.Add("Rainfall around " + Units.FormatPrecipitation(day.PrecipitationInches) + ".");
            }

            if (day.WindSpeedMph.HasValue)
            {
                parts.Add("Wind " + Units.DegreesToCardinal(day.WindDirectionDegrees)
                          + " up to " + Units.FormatSpeed(day.WindSpeedMph) + ".");
            }

            return string.Join(" ", parts.ToArray());
        }

        // ---- geometry --------------------------------------------------------

        private List<ForecastDay> Days()
        {
            return Snapshot.Days.Take(10).ToList();
        }

        private Rectangle ListBounds()
        {
            int height = ClientSize.Height - DetailHeight - Gutter * 3;
            return new Rectangle(Gutter, Gutter, ClientSize.Width - Gutter * 2, Math.Max(60, height));
        }

        private int RowHeight(int count)
        {
            if (count <= 0) return MinRowHeight;

            int available = ListBounds().Height - 12;
            return Math.Max(MinRowHeight, available / count);
        }

        private int IndexAt(int y)
        {
            if (Snapshot == null || Snapshot.Days.Count == 0) return -1;

            List<ForecastDay> days = Days();
            Rectangle listBounds = ListBounds();
            int rowHeight = RowHeight(days.Count);

            int offset = y - (listBounds.Y + 6);
            if (offset < 0) return -1;

            int index = offset / rowHeight;
            return index >= 0 && index < days.Count ? index : -1;
        }

        private static void TemperatureRange(List<ForecastDay> days, out double minimum, out double maximum)
        {
            minimum = double.MaxValue;
            maximum = double.MinValue;

            foreach (ForecastDay day in days)
            {
                if (day.LowF.HasValue) minimum = Math.Min(minimum, day.LowF.Value);
                if (day.HighF.HasValue) maximum = Math.Max(maximum, day.HighF.Value);
            }

            if (minimum > maximum)
            {
                minimum = 0d;
                maximum = 1d;
            }

            // A little headroom stops the hottest day's bar running to the very edge.
            double padding = Math.Max(2d, (maximum - minimum) * 0.06);
            minimum -= padding;
            maximum += padding;
        }
    }
}
