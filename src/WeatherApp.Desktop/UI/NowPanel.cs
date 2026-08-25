using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;
using WeatherApp.UI.Rendering;

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

            List<HourlyPoint> hours = HourlyChartRenderer.Upcoming(Snapshot, 24);
            if (hours.Count < 2)
            {
                AppTheme.DrawText(context, "Hourly data is unavailable.", AppTheme.Regular, AppTheme.SizeBody,
                    AppTheme.TextMuted, new Rect(inner.X, inner.Y + 32, inner.Width, 22));
                return;
            }

            var plot = new Rect(inner.X, inner.Y + 26, inner.Width, inner.Height - 50);
            if (plot.Width <= 10 || plot.Height <= 20) return;

            // Bars, temperature line and hour labels come from the shared renderer,
            // so this chart is identical on desktop and on a phone.
            HourlyChartRenderer.Draw(context, plot, hours);
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
    }
}
