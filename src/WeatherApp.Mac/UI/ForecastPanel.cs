using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// The ten-day forecast: highs and lows drawn as bars against the range of the
    /// whole period, with the selected day's narrative underneath.
    /// </summary>
    public sealed class ForecastPanel : WeatherPanel
    {
        private const double Gutter = 16;
        private const double DetailHeight = 128;
        private const double MinRowHeight = 36;

        private int _selectedIndex;
        private int _hoverIndex = -1;

        public ForecastPanel()
        {
            Cursor = new Cursor(StandardCursorType.Hand);

            PointerMoved += (s, e) =>
            {
                int index = IndexAt(e.GetPosition(this).Y);
                if (index == _hoverIndex) return;

                _hoverIndex = index;
                InvalidateVisual();
            };

            PointerExited += (s, e) =>
            {
                if (_hoverIndex == -1) return;

                _hoverIndex = -1;
                InvalidateVisual();
            };

            PointerPressed += (s, e) =>
            {
                int index = IndexAt(e.GetPosition(this).Y);
                if (index < 0 || index == _selectedIndex) return;

                _selectedIndex = index;
                InvalidateVisual();
            };
        }

        protected override void OnSnapshotChanged()
        {
            _selectedIndex = 0;
            _hoverIndex = -1;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (Snapshot == null || Snapshot.Days.Count == 0)
            {
                DrawPlaceholder(context, "No forecast available.");
                return;
            }

            List<ForecastDay> days = Days();
            Rect listBounds = ListBounds();
            Theme.DrawCard(context, listBounds);

            double rowHeight = RowHeight(days.Count);
            double minimum, maximum;
            TemperatureRange(days, out minimum, out maximum);

            for (int i = 0; i < days.Count; i++)
            {
                var row = new Rect(
                    listBounds.X + 1, listBounds.Y + 6 + i * rowHeight,
                    listBounds.Width - 2, rowHeight);

                if (row.Bottom > listBounds.Bottom - 4) break;
                DrawRow(context, row, days[i], i, minimum, maximum);
            }

            DrawDetail(context, days);
        }

        private void DrawRow(DrawingContext context, Rect bounds, ForecastDay day,
            int index, double minimum, double maximum)
        {
            if (index == _selectedIndex)
            {
                context.DrawRectangle(Theme.SurfaceAltBrush, null,
                    bounds.Deflate(new Thickness(5, 1)), 4, 4);
            }
            else if (index == _hoverIndex)
            {
                context.DrawRectangle(Theme.Brush(Theme.SurfaceHover, 60), null,
                    bounds.Deflate(new Thickness(5, 1)), 4, 4);
            }

            double x = bounds.X + 16;
            double top = bounds.Y;
            double height = bounds.Height;

            Theme.DrawLineText(context, day.DayLabel, Theme.Bold, Theme.SizeBody, Theme.Text,
                new Rect(x, top, 82, height));
            x += 82;

            Theme.DrawLineText(context, day.Date.ToString("MMM d"), Theme.Regular, Theme.SizeSmall,
                Theme.TextFaint, new Rect(x, top, 62, height));
            x += 62;

            Theme.DrawLineText(context, WeatherCodes.Glyph(day.WeatherCode, true), Theme.Regular,
                Theme.SizeBody, Theme.Accent, new Rect(x, top, 26, height), TextAlignment.Center);
            x += 30;

            double summaryWidth = Math.Max(80, bounds.Width - x - 360);
            Theme.DrawLineText(context, day.Summary ?? "--", Theme.Regular, Theme.SizeBody,
                Theme.Text, new Rect(x, top, summaryWidth, height));
            x += summaryWidth + 10;

            bool wet = day.PrecipitationProbability.HasValue && day.PrecipitationProbability.Value >= 30;
            string pop = day.PrecipitationProbability.HasValue && day.PrecipitationProbability.Value > 0
                ? Units.FormatPercent(day.PrecipitationProbability)
                : "--";

            Theme.DrawLineText(context, pop, Theme.Bold, Theme.SizeBody,
                wet ? Theme.Rain : Theme.TextFaint,
                new Rect(x, top, 54, height), TextAlignment.Right);
            x += 62;

            Theme.DrawLineText(context, Units.FormatTemperature(day.LowF), Theme.Regular,
                Theme.SizeBody, Theme.TextMuted, new Rect(x, top, 48, height), TextAlignment.Right);
            x += 54;

            double barWidth = Math.Max(40, bounds.Right - x - 68);
            DrawRangeBar(context, new Rect(x, top + height / 2 - 4, barWidth, 8), day, minimum, maximum);
            x += barWidth + 10;

            Theme.DrawLineText(context, Units.FormatTemperature(day.HighF), Theme.Bold,
                Theme.SizeBody, Theme.Text, new Rect(x, top, 50, height));
        }

        /// <summary>
        /// One day's high-to-low span against the whole period's range, so the bars
        /// line up into a temperature profile down the column.
        /// </summary>
        private static void DrawRangeBar(DrawingContext context, Rect track,
            ForecastDay day, double minimum, double maximum)
        {
            context.DrawRectangle(Theme.Brush(Theme.BorderColor, 70), null, track, 4, 4);

            if (!day.HighF.HasValue || !day.LowF.HasValue) return;

            double span = Math.Max(1d, maximum - minimum);
            double left = track.X + track.Width * ((day.LowF.Value - minimum) / span);
            double right = track.X + track.Width * ((day.HighF.Value - minimum) / span);

            if (right - left < 6) right = left + 6;
            if (right > track.Right)
            {
                right = track.Right;
                left = Math.Max(track.X, right - 6);
            }

            // Cold end blue, warm end orange, so the gradient itself reads as
            // temperature. Anchored to the whole track so colour means the same
            // thing on every row rather than restarting inside each bar.
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(track.X, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(track.Right, 0, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x4F, 0x9B, 0xE0), 0),
                    new GradientStop(Color.FromRgb(0xE8, 0x8B, 0x3A), 1)
                }
            };

            context.DrawRectangle(brush, null, new Rect(left, track.Y, right - left, track.Height), 4, 4);
        }

        private void DrawDetail(DrawingContext context, List<ForecastDay> days)
        {
            var bounds = new Rect(Gutter, H - DetailHeight - Gutter, W - Gutter * 2, DetailHeight);
            if (bounds.Width <= 20 || bounds.Y < Gutter) return;

            Theme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(18, 14));

            int index = Math.Min(Math.Max(0, _selectedIndex), days.Count - 1);
            ForecastDay day = days[index];

            string heading = day.Date.ToString("dddd, MMMM d");
            if (!string.IsNullOrEmpty(day.Source)) heading += "   ·   " + day.Source;

            Theme.DrawLineText(context, heading, Theme.Bold, Theme.SizeSmall, Theme.TextMuted,
                new Rect(inner.X, inner.Y, inner.Width, 18), middle: false);

            string narrative = !string.IsNullOrWhiteSpace(day.DetailedForecast)
                ? day.DetailedForecast
                : BuildFallbackNarrative(day);

            Theme.DrawText(context, narrative, Theme.Regular, Theme.SizeBody, Theme.Text,
                new Rect(inner.X, inner.Y + 24, inner.Width, inner.Height - 24));
        }

        /// <summary>
        /// Days past the NWS window carry no narrative, so one is assembled from
        /// the model values rather than leaving the pane blank.
        /// </summary>
        private static string BuildFallbackNarrative(ForecastDay day)
        {
            var parts = new List<string> { (day.Summary ?? "No summary available") + "." };

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

            return string.Join(" ", parts);
        }

        // ---- geometry --------------------------------------------------------

        private List<ForecastDay> Days()
        {
            return Snapshot.Days.Take(10).ToList();
        }

        private Rect ListBounds()
        {
            double height = H - DetailHeight - Gutter * 3;
            return new Rect(Gutter, Gutter, Math.Max(10, W - Gutter * 2), Math.Max(60, height));
        }

        private double RowHeight(int count)
        {
            if (count <= 0) return MinRowHeight;
            return Math.Max(MinRowHeight, (ListBounds().Height - 12) / count);
        }

        private int IndexAt(double y)
        {
            if (Snapshot == null || Snapshot.Days.Count == 0) return -1;

            List<ForecastDay> days = Days();
            double offset = y - (ListBounds().Y + 6);
            if (offset < 0) return -1;

            int index = (int)(offset / RowHeight(days.Count));
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

            // Headroom so the hottest day's bar does not run to the very edge.
            double padding = Math.Max(2d, (maximum - minimum) * 0.06);
            minimum -= padding;
            maximum += padding;
        }
    }
}
