using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// The ten-day forecast as tappable rows. Tapping a day expands it in place
    /// to show that day's precipitation split and the forecast office's narrative.
    ///
    /// This is where the desktop head's nine-column precipitation table goes on a
    /// phone: the table cannot survive a narrow screen, but the same numbers read
    /// perfectly well as an expanded row, and they stay next to the day they
    /// describe instead of living on a separate tab.
    /// </summary>
    public sealed class PhoneForecastScreen : PhoneScreen
    {
        private const double Gutter = 14;
        private const double RowHeight = 62;
        private const double ExpandedExtra = 188;

        private int _expandedIndex = -1;

        protected override void OnSnapshotChanged()
        {
            _expandedIndex = -1;
            ResetScroll();
        }

        private List<ForecastDay> Days
        {
            get
            {
                return Snapshot == null
                    ? new List<ForecastDay>()
                    : Snapshot.Days.Take(10).ToList();
            }
        }

        protected override double ContentHeight
        {
            get
            {
                int count = Days.Count;
                if (count == 0) return H;

                double height = Gutter + count * RowHeight + Gutter;
                if (_expandedIndex >= 0) height += ExpandedExtra;
                return height;
            }
        }

        protected override void OnTap(Point point)
        {
            List<ForecastDay> days = Days;
            if (days.Count == 0) return;

            double y = Gutter;
            for (int i = 0; i < days.Count; i++)
            {
                double rowHeight = RowHeight + (i == _expandedIndex ? ExpandedExtra : 0);

                if (point.Y >= y && point.Y < y + rowHeight)
                {
                    _expandedIndex = _expandedIndex == i ? -1 : i;
                    InvalidateSurface();
                    return;
                }
                y += rowHeight;
            }
        }

        protected override void DrawContent(DrawingContext context, double width)
        {
            List<ForecastDay> days = Days;
            if (days.Count == 0)
            {
                DrawPlaceholder(context, "No forecast available.");
                return;
            }

            double inner = width - Gutter * 2;
            if (inner < 60) return;

            double minimum, maximum;
            TemperatureRange(days, out minimum, out maximum);

            double y = Gutter;
            for (int i = 0; i < days.Count; i++)
            {
                bool expanded = i == _expandedIndex;
                double rowHeight = RowHeight + (expanded ? ExpandedExtra : 0);

                var bounds = new Rect(Gutter, y, inner, rowHeight);
                AppTheme.DrawCard(context, bounds,
                    expanded ? AppTheme.SurfaceAltBrush : AppTheme.SurfaceBrush);

                DrawRow(context, new Rect(Gutter, y, inner, RowHeight), days[i], minimum, maximum, expanded);

                if (expanded)
                {
                    DrawExpanded(context,
                        new Rect(Gutter + 14, y + RowHeight - 6, inner - 28, ExpandedExtra - 8), days[i], i);
                }

                y += rowHeight;
            }
        }

        private static void DrawRow(DrawingContext context, Rect bounds, ForecastDay day,
            double minimum, double maximum, bool expanded)
        {
            Rect inner = bounds.Deflate(new Thickness(14, 8));

            AppTheme.DrawLineText(context, day.DayLabel, AppTheme.Bold, AppTheme.SizeBody,
                AppTheme.Text, new Rect(inner.X, inner.Y, 110, 22), middle: false);

            AppTheme.DrawLineText(context, day.Date.ToString("MMM d"), AppTheme.Regular,
                AppTheme.SizeSmall, AppTheme.TextFaint,
                new Rect(inner.X, inner.Y + 22, 110, 18), middle: false);

            AppTheme.DrawLineText(context, WeatherCodes.Glyph(day.WeatherCode, true), AppTheme.Regular,
                AppTheme.SizeBody, AppTheme.Accent,
                new Rect(inner.X + 112, inner.Y + 4, 26, 26), TextAlignment.Center);

            double summaryWidth = Math.Max(60, inner.Width - 250);
            AppTheme.DrawLineText(context, day.Summary ?? "--", AppTheme.Regular, AppTheme.SizeBody,
                AppTheme.TextMuted, new Rect(inner.X + 142, inner.Y + 4, summaryWidth, 22), middle: false);

            if (day.PrecipitationProbability.HasValue && day.PrecipitationProbability.Value > 0)
            {
                AppTheme.DrawLineText(context, Units.FormatPercent(day.PrecipitationProbability),
                    AppTheme.Bold, AppTheme.SizeSmall,
                    day.PrecipitationProbability.Value >= 30 ? AppTheme.Rain : AppTheme.TextFaint,
                    new Rect(inner.X + 142, inner.Y + 24, summaryWidth, 18), middle: false);
            }

            // High and low sit right-aligned, with the range bar under them.
            AppTheme.DrawLineText(context,
                Units.FormatTemperature(day.HighF) + "  " + Units.FormatTemperature(day.LowF),
                AppTheme.Bold, AppTheme.SizeBody, AppTheme.Text,
                new Rect(inner.Right - 104, inner.Y, 104, 22), TextAlignment.Right, middle: false);

            DrawRangeBar(context, new Rect(inner.Right - 104, inner.Y + 28, 104, 6),
                day, minimum, maximum);

            // A chevron, so the row reads as tappable.
            AppTheme.DrawLineText(context, expanded ? "⌃" : "⌄", AppTheme.Regular,
                AppTheme.SizeBody, AppTheme.TextFaint,
                new Rect(inner.Right - 12, inner.Y + 4, 12, 22), TextAlignment.Center);
        }

        private static void DrawRangeBar(DrawingContext context, Rect track,
            ForecastDay day, double minimum, double maximum)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.BorderColor, 70), null, track, 3, 3);
            if (!day.HighF.HasValue || !day.LowF.HasValue) return;

            double span = Math.Max(1d, maximum - minimum);
            double left = track.X + track.Width * ((day.LowF.Value - minimum) / span);
            double right = track.X + track.Width * ((day.HighF.Value - minimum) / span);
            if (right - left < 5) right = left + 5;
            if (right > track.Right)
            {
                right = track.Right;
                left = Math.Max(track.X, right - 5);
            }

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

            context.DrawRectangle(brush, null, new Rect(left, track.Y, right - left, track.Height), 3, 3);
        }

        /// <summary>The precipitation breakdown and narrative for an opened day.</summary>
        private void DrawExpanded(DrawingContext context, Rect bounds, ForecastDay day, int index)
        {
            PrecipitationDay precip = Snapshot.Precipitation.Days
                .FirstOrDefault(d => d.Date.Date == day.Date.Date);

            double y = bounds.Y;

            if (precip != null)
            {
                var chips = new[]
                {
                    Tuple.Create("Rain", precip.RainChance, AppTheme.RainColor),
                    Tuple.Create("Snow", precip.SnowChance, AppTheme.SnowColor),
                    Tuple.Create("Ice", precip.IcyChance, AppTheme.IceColor),
                    Tuple.Create("Storms", precip.ThunderstormChance, AppTheme.ThunderColor),
                    Tuple.Create("Hail", precip.SevereHailChance, AppTheme.HailColor)
                };

                double chipWidth = Math.Max(56, (bounds.Width - 8 * 4) / 5);
                double x = bounds.X;

                foreach (var chip in chips)
                {
                    Color tint = chip.Item2.HasValue ? chip.Item3 : AppTheme.TextFaintColor;
                    var box = new Rect(x, y, chipWidth, 46);

                    if (chip.Item2.HasValue)
                    {
                        context.DrawRectangle(AppTheme.Brush(tint, 40),
                            new Pen(AppTheme.Brush(tint, 110)), box, 6, 6);
                    }

                    AppTheme.DrawLineText(context, chip.Item1, AppTheme.Regular, AppTheme.SizeSmall,
                        AppTheme.TextFaint, new Rect(box.X, box.Y + 5, box.Width, 15),
                        TextAlignment.Center, middle: false);

                    AppTheme.DrawLineText(context,
                        chip.Item2.HasValue ? Math.Round(chip.Item2.Value).ToString("0") + "%" : "--",
                        AppTheme.Bold, AppTheme.SizeBody, AppTheme.Brush(tint),
                        new Rect(box.X, box.Y + 21, box.Width, 20), TextAlignment.Center, middle: false);

                    x += chipWidth + 8;
                }

                y += 54;

                if (!string.IsNullOrEmpty(precip.SevereRiskCategory))
                {
                    AppTheme.DrawLineText(context, "SPC severe risk: " + precip.SevereRiskCategory,
                        AppTheme.Bold, AppTheme.SizeSmall, AppTheme.Brush(AppTheme.WarningColor),
                        new Rect(bounds.X, y, bounds.Width, 18), middle: false);
                    y += 22;
                }
            }

            string narrative = !string.IsNullOrWhiteSpace(day.DetailedForecast)
                ? day.DetailedForecast
                : (day.Summary ?? "No further detail available.");

            AppTheme.DrawText(context, narrative, AppTheme.Regular, AppTheme.SizeBody,
                AppTheme.Text, new Rect(bounds.X, y, bounds.Width, Math.Max(0, bounds.Bottom - y - 4)));
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

            double padding = Math.Max(2d, (maximum - minimum) * 0.06);
            minimum -= padding;
            maximum += padding;
        }
    }
}
