using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Current conditions plus the next 24 hours as a combined temperature and
    /// precipitation-chance chart. Port of the WinForms panel of the same name;
    /// the layout arithmetic is unchanged and only the drawing calls differ.
    /// </summary>
    public sealed class NowPanel : WeatherPanel
    {
        private const double Gutter = 16;
        private const double HeaderHeight = 190;
        private const double ChartHeight = 220;

        protected override void DrawSurface(DrawingContext context)
        {
            if (Snapshot == null)
            {
                DrawPlaceholder(context, "Loading conditions...");
                return;
            }

            double width = W - Gutter * 2;
            if (width <= 40) return;

            var header = new Rect(Gutter, Gutter, width, HeaderHeight);
            DrawHeader(context, header);

            double y = header.Bottom + Gutter;
            var chart = new Rect(Gutter, y, width, ChartHeight);
            DrawHourlyChart(context, chart);

            y = chart.Bottom + Gutter;
            double detailHeight = Math.Max(0, H - y - Gutter);
            if (detailHeight > 60) DrawDetails(context, new Rect(Gutter, y, width, detailHeight));
        }

        private void DrawHeader(DrawingContext context, Rect bounds)
        {
            AppTheme.DrawCard(context, bounds);

            Rect inner = bounds.Deflate(new Thickness(20, 16));
            CurrentConditions current = Snapshot.Current;

            string place = Snapshot.Location != null ? Snapshot.Location.DisplayName : "Unknown location";
            AppTheme.DrawLineText(context, place, AppTheme.Regular, AppTheme.SizeTitle, AppTheme.Text,
                new Rect(inner.X, inner.Y, inner.Width, 32), middle: false);

            if (current == null)
            {
                AppTheme.DrawText(context, "Current conditions are unavailable.", AppTheme.Regular,
                    AppTheme.SizeBody, AppTheme.TextMuted, new Rect(inner.X, inner.Y + 44, inner.Width, 24));
                return;
            }

            // Temperature, oversized: the one number people open the app for.
            AppTheme.DrawLineText(context, Units.FormatTemperature(current.TemperatureF),
                AppTheme.Regular, AppTheme.SizeHuge, AppTheme.Text,
                new Rect(inner.X, inner.Y + 34, 230, 76));

            double textX = inner.X + 218;

            AppTheme.DrawLineText(context, WeatherCodes.Glyph(current.WeatherCode, current.IsDaytime),
                AppTheme.Regular, AppTheme.SizeGlyph, AppTheme.Accent,
                new Rect(textX, inner.Y + 42, 42, 38), TextAlignment.Center);

            AppTheme.DrawLineText(context, current.Summary ?? "--", AppTheme.Bold, AppTheme.SizeHeading, AppTheme.Text,
                new Rect(textX + 48, inner.Y + 42, Math.Max(40, inner.Right - textX - 48), 28));

            var extras = new List<string>();
            if (current.FeelsLikeF.HasValue)
            {
                extras.Add("Feels like " + Units.FormatTemperature(current.FeelsLikeF));
            }
            if (current.WindSpeedMph.HasValue)
            {
                extras.Add("Wind " + current.WindDirectionCardinal + " " + Units.FormatSpeed(current.WindSpeedMph));
            }
            if (current.WindGustMph.HasValue && current.WindGustMph.Value > (current.WindSpeedMph ?? 0) + 3)
            {
                extras.Add("gusting " + Units.FormatSpeed(current.WindGustMph));
            }

            AppTheme.DrawLineText(context, string.Join("   ·   ", extras), AppTheme.Regular,
                AppTheme.SizeBody, AppTheme.TextMuted,
                new Rect(textX + 48, inner.Y + 74, Math.Max(40, inner.Right - textX - 48), 24));

            ForecastDay today = Snapshot.Days.FirstOrDefault();
            if (today != null)
            {
                string range = "Today  " + Units.FormatTemperature(today.HighF)
                               + " / " + Units.FormatTemperature(today.LowF);
                AppTheme.DrawLineText(context, range, AppTheme.Bold, AppTheme.SizeBody, AppTheme.Text,
                    new Rect(textX + 48, inner.Y + 102, Math.Max(40, inner.Right - textX - 48), 24));
            }

            string stamp = "Updated " + Snapshot.RetrievedAt.ToString("h:mm tt")
                           + (string.IsNullOrEmpty(current.Source) ? string.Empty : "  ·  " + current.Source);
            AppTheme.DrawLineText(context, stamp, AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextFaint,
                new Rect(inner.X, inner.Bottom - 20, inner.Width, 18));
        }

        /// <summary>
        /// Precipitation probability as bars, temperature as a line over the top,
        /// with hours expecting snow, ice or thunder coloured accordingly.
        /// </summary>
        private void DrawHourlyChart(DrawingContext context, Rect bounds)
        {
            AppTheme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(18, 14));

            AppTheme.DrawLineText(context, "NEXT 24 HOURS", AppTheme.Bold, AppTheme.SizeSmall, AppTheme.TextMuted,
                new Rect(inner.X, inner.Y, inner.Width, 18), middle: false);

            List<HourlyPoint> hours = UpcomingHours(24);
            if (hours.Count < 2)
            {
                AppTheme.DrawText(context, "Hourly data is unavailable.", AppTheme.Regular, AppTheme.SizeBody,
                    AppTheme.TextMuted, new Rect(inner.X, inner.Y + 32, inner.Width, 22));
                return;
            }

            var plot = new Rect(inner.X, inner.Y + 26, inner.Width, inner.Height - 50);
            if (plot.Width <= 10 || plot.Height <= 20) return;

            double columnWidth = plot.Width / hours.Count;

            // Probability bars occupy the lower 40% so the temperature line stays readable.
            double barZoneTop = plot.Y + plot.Height * 0.60;
            double barZoneHeight = plot.Bottom - barZoneTop;

            for (int i = 0; i < hours.Count; i++)
            {
                HourlyPoint hour = hours[i];
                double probability = hour.PrecipitationProbability ?? 0d;
                if (probability <= 0) continue;

                double barHeight = Math.Max(2, barZoneHeight * Math.Min(probability, 100d) / 100d);

                Color color = AppTheme.RainColor;
                if ((hour.SnowfallInches ?? 0) > 0.01 || WeatherCodes.IsSnow(hour.WeatherCode))
                {
                    color = AppTheme.SnowColor;
                }
                else if (WeatherCodes.IsFreezing(hour.WeatherCode)) color = AppTheme.IceColor;
                else if (hour.IsThunder) color = AppTheme.ThunderColor;

                var bar = new Rect(
                    plot.X + i * columnWidth + 1,
                    plot.Bottom - barHeight,
                    Math.Max(2, columnWidth - 2),
                    barHeight);

                context.DrawRectangle(AppTheme.Brush(color, 190), null, bar);
            }

            DrawTemperatureLine(context, plot, hours, columnWidth, barZoneTop);
            DrawHourLabels(context, plot, hours, columnWidth);
        }

        private static void DrawTemperatureLine(DrawingContext context, Rect plot,
            List<HourlyPoint> hours, double columnWidth, double lineZoneBottom)
        {
            var temperatures = hours
                .Where(h => h.TemperatureF.HasValue)
                .Select(h => h.TemperatureF.Value)
                .ToList();

            if (temperatures.Count < 2) return;

            double min = temperatures.Min();
            double max = temperatures.Max();
            double span = Math.Max(1d, max - min);

            double lineZoneTop = plot.Y + 16;
            double lineZoneHeight = Math.Max(10, lineZoneBottom - lineZoneTop - 8);

            var points = new List<Point>();
            for (int i = 0; i < hours.Count; i++)
            {
                if (!hours[i].TemperatureF.HasValue) continue;

                double x = plot.X + i * columnWidth + columnWidth / 2d;
                double y = lineZoneTop + lineZoneHeight * (1d - (hours[i].TemperatureF.Value - min) / span);
                points.Add(new Point(x, y));
            }

            if (points.Count < 2) return;

            // Avalonia has no polyline primitive on DrawingContext; successive
            // segments are equivalent here and avoid building a geometry per frame.
            var pen = new Pen(AppTheme.Accent, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (int i = 1; i < points.Count; i++)
            {
                context.DrawLine(pen, points[i - 1], points[i]);
            }

            // Label only the extremes; every hour is unreadable at this size.
            int hottest = temperatures.IndexOf(max);
            int coldest = temperatures.IndexOf(min);

            LabelPoint(context, points, hottest, max);
            if (coldest != hottest) LabelPoint(context, points, coldest, min);
        }

        private static void LabelPoint(DrawingContext context, List<Point> points, int index, double value)
        {
            if (index < 0 || index >= points.Count) return;

            Point point = points[index];
            AppTheme.DrawLineText(context, Units.FormatTemperature(value), AppTheme.Bold, AppTheme.SizeSmall,
                AppTheme.Text, new Rect(point.X - 26, point.Y - 24, 52, 18), TextAlignment.Center);
        }

        private static void DrawHourLabels(DrawingContext context, Rect plot,
            List<HourlyPoint> hours, double columnWidth)
        {
            // A label every few hours keeps the axis legible at any window width.
            int step = Math.Max(1, (int)Math.Ceiling(58d / Math.Max(1d, columnWidth)));

            for (int i = 0; i < hours.Count; i += step)
            {
                var bounds = new Rect(
                    plot.X + i * columnWidth - 16, plot.Bottom + 4, columnWidth + 32, 18);

                AppTheme.DrawLineText(context, hours[i].Time.ToString("htt").ToLowerInvariant(),
                    AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextFaint, bounds, TextAlignment.Center);
            }
        }

        private void DrawDetails(DrawingContext context, Rect bounds)
        {
            AppTheme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(18, 14));

            CurrentConditions current = Snapshot.Current;
            if (current == null) return;

            var entries = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Humidity", Units.FormatPercent(current.RelativeHumidity)),
                new KeyValuePair<string, string>("Dew point", Units.FormatTemperature(current.DewPointF)),
                new KeyValuePair<string, string>("Pressure", Units.FormatPressure(current.PressureMb)),
                new KeyValuePair<string, string>("Cloud cover", Units.FormatPercent(current.CloudCoverPercent)),
                new KeyValuePair<string, string>("Visibility", Units.FormatDistance(current.VisibilityMiles)),
                new KeyValuePair<string, string>("Wind gust", Units.FormatSpeed(current.WindGustMph))
            };

            ForecastDay today = Snapshot.Days.FirstOrDefault();
            if (today != null)
            {
                if (today.Sunrise.HasValue)
                {
                    entries.Add(new KeyValuePair<string, string>(
                        "Sunrise", today.Sunrise.Value.ToString("h:mm tt")));
                }
                if (today.Sunset.HasValue)
                {
                    entries.Add(new KeyValuePair<string, string>(
                        "Sunset", today.Sunset.Value.ToString("h:mm tt")));
                }
                if (today.UvIndexMax.HasValue)
                {
                    entries.Add(new KeyValuePair<string, string>(
                        "Max UV index", Math.Round(today.UvIndexMax.Value).ToString("0")));
                }
            }

            int columns = Math.Max(2, Math.Min(4, (int)(inner.Width / 190)));
            double columnWidth = inner.Width / columns;
            const double RowHeight = 44;

            for (int i = 0; i < entries.Count; i++)
            {
                double x = inner.X + (i % columns) * columnWidth;
                double y = inner.Y + (i / columns) * RowHeight;
                if (y + RowHeight > inner.Bottom) break;

                AppTheme.DrawLineText(context, entries[i].Key.ToUpperInvariant(), AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(x, y, columnWidth - 12, 16));
                AppTheme.DrawLineText(context, entries[i].Value, AppTheme.Bold, AppTheme.SizeBody,
                    AppTheme.Text, new Rect(x, y + 18, columnWidth - 12, 22));
            }
        }

        /// <summary>The next N hours from now, at the forecast location's own clock.</summary>
        private List<HourlyPoint> UpcomingHours(int count)
        {
            if (Snapshot == null) return new List<HourlyPoint>();

            DateTime reference = Snapshot.Current != null
                ? Snapshot.Current.ObservedAt.AddHours(-1)
                : DateTime.Now.AddHours(-1);

            return Snapshot.Hours
                .Where(h => h.Time >= reference)
                .OrderBy(h => h.Time)
                .Take(count)
                .ToList();
        }
    }
}
