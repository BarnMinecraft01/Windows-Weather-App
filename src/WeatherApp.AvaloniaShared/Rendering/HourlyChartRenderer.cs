using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI.Rendering
{
    /// <summary>
    /// Draws the next N hours as precipitation-probability bars with a
    /// temperature line over the top.
    ///
    /// Shared between the desktop and phone heads: a column of numbers makes you
    /// read every row to find when the rain arrives, whereas the shape of the
    /// probability bars answers that at a glance, and that is worth the same on
    /// both. Bars are coloured by precipitation type, so a snow event does not
    /// look like a rain event.
    /// </summary>
    public static class HourlyChartRenderer
    {
        /// <summary>
        /// Renders into <paramref name="plot"/>, leaving room below it for the
        /// hour labels which are drawn just outside the bottom edge.
        /// </summary>
        public static void Draw(DrawingContext context, Rect plot, IList<HourlyPoint> hours)
        {
            if (hours == null || hours.Count < 2) return;
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

                context.DrawRectangle(AppTheme.Brush(TypeColor(hour), 190), null,
                    new Rect(plot.X + i * columnWidth + 1,
                             plot.Bottom - barHeight,
                             Math.Max(2, columnWidth - 2),
                             barHeight));
            }

            DrawTemperatureLine(context, plot, hours, columnWidth, barZoneTop);
            DrawHourLabels(context, plot, hours, columnWidth);
        }

        /// <summary>The bar colour for an hour, keyed off what is expected to fall.</summary>
        private static Color TypeColor(HourlyPoint hour)
        {
            if ((hour.SnowfallInches ?? 0) > 0.01 || WeatherCodes.IsSnow(hour.WeatherCode))
            {
                return AppTheme.SnowColor;
            }
            if (WeatherCodes.IsFreezing(hour.WeatherCode)) return AppTheme.IceColor;
            if (hour.IsThunder) return AppTheme.ThunderColor;
            return AppTheme.RainColor;
        }

        private static void DrawTemperatureLine(DrawingContext context, Rect plot,
            IList<HourlyPoint> hours, double columnWidth, double lineZoneBottom)
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
            AppTheme.DrawLineText(context, Units.FormatTemperature(value), AppTheme.Bold,
                AppTheme.SizeSmall, AppTheme.Text,
                new Rect(point.X - 26, point.Y - 24, 52, 18), TextAlignment.Center);
        }

        private static void DrawHourLabels(DrawingContext context, Rect plot,
            IList<HourlyPoint> hours, double columnWidth)
        {
            // A label every few hours keeps the axis legible at any width, including
            // a phone in portrait where the columns are only a few points wide.
            int step = Math.Max(1, (int)Math.Ceiling(58d / Math.Max(1d, columnWidth)));

            for (int i = 0; i < hours.Count; i += step)
            {
                AppTheme.DrawLineText(context, hours[i].Time.ToString("htt").ToLowerInvariant(),
                    AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextFaint,
                    new Rect(plot.X + i * columnWidth - 16, plot.Bottom + 4, columnWidth + 32, 18),
                    TextAlignment.Center);
            }
        }

        /// <summary>
        /// The next N hours from the snapshot, at the forecast location's clock.
        /// Shared so both heads pick the same window.
        /// </summary>
        public static List<HourlyPoint> Upcoming(WeatherSnapshot snapshot, int count)
        {
            if (snapshot == null) return new List<HourlyPoint>();

            DateTime reference = snapshot.Current != null
                ? snapshot.Current.ObservedAt.AddHours(-1)
                : DateTime.Now.AddHours(-1);

            return snapshot.Hours
                .Where(h => h.Time >= reference)
                .OrderBy(h => h.Time)
                .Take(count)
                .ToList();
        }
    }
}
