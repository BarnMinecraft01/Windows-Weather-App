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
    /// Current conditions, plus the next 24 hours as a combined temperature and
    /// precipitation-chance chart.
    ///
    /// The hourly chart is the part that earns its place: a column of numbers makes
    /// you read every row to find when the rain arrives, whereas the shape of the
    /// probability bars answers that in a glance.
    /// </summary>
    public sealed class NowPanel : WeatherPanel
    {
        private const int Gutter = 14;
        private const int HeaderHeight = 168;
        private const int ChartHeight = 200;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);
            g.Clear(Theme.Background);

            if (Snapshot == null)
            {
                Theme.DrawText(g, "Loading conditions...", Theme.FontBody, Theme.TextMuted,
                    ClientRectangle, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            int width = ClientSize.Width - Gutter * 2;
            if (width <= 40) return;

            var header = new Rectangle(Gutter, Gutter, width, HeaderHeight);
            DrawHeader(g, header);

            int y = header.Bottom + Gutter;
            var chart = new Rectangle(Gutter, y, width, ChartHeight);
            DrawHourlyChart(g, chart);

            y = chart.Bottom + Gutter;
            var details = new Rectangle(Gutter, y, width, Math.Max(0, ClientSize.Height - y - Gutter));
            DrawDetails(g, details);
        }

        private void DrawHeader(Graphics g, Rectangle bounds)
        {
            Theme.DrawCard(g, bounds);

            CurrentConditions current = Snapshot.Current;
            var inner = Rectangle.Inflate(bounds, -18, -14);

            string place = Snapshot.Location != null ? Snapshot.Location.DisplayName : "Unknown location";
            Theme.DrawText(g, place, Theme.FontTitle, Theme.Text,
                new Rectangle(inner.X, inner.Y, inner.Width, 30), wrap: false);

            if (current == null)
            {
                Theme.DrawText(g, "Current conditions are unavailable.", Theme.FontBody, Theme.TextMuted,
                    new Rectangle(inner.X, inner.Y + 40, inner.Width, 24));
                return;
            }

            // Temperature, oversized -- it is the one number people open the app for.
            string temperature = Units.FormatTemperature(current.TemperatureF);
            var temperatureBounds = new Rectangle(inner.X, inner.Y + 30, 210, 70);
            Theme.DrawText(g, temperature, Theme.FontHuge, Theme.Text, temperatureBounds,
                StringAlignment.Near, StringAlignment.Center, false);

            int textX = inner.X + 200;

            string glyph = WeatherCodes.Glyph(current.WeatherCode, current.IsDaytime);
            Theme.DrawText(g, glyph, Theme.FontGlyph, Theme.Accent,
                new Rectangle(textX, inner.Y + 38, 40, 34), StringAlignment.Center, StringAlignment.Center, false);

            Theme.DrawText(g, current.Summary ?? "--", Theme.FontHeading, Theme.Text,
                new Rectangle(textX + 44, inner.Y + 38, inner.Width - textX - 40, 26), wrap: false);

            var feels = new List<string>();
            if (current.FeelsLikeF.HasValue)
            {
                feels.Add("Feels like " + Units.FormatTemperature(current.FeelsLikeF));
            }
            if (current.WindSpeedMph.HasValue)
            {
                feels.Add("Wind " + current.WindDirectionCardinal + " " + Units.FormatSpeed(current.WindSpeedMph));
            }
            if (current.WindGustMph.HasValue && current.WindGustMph.Value > (current.WindSpeedMph ?? 0) + 3)
            {
                feels.Add("gusting " + Units.FormatSpeed(current.WindGustMph));
            }

            Theme.DrawText(g, string.Join("   ·   ", feels.ToArray()), Theme.FontBody, Theme.TextMuted,
                new Rectangle(textX + 44, inner.Y + 66, inner.Width - textX - 40, 22), wrap: false);

            // Today's high and low, pulled from the first forecast day.
            ForecastDay today = Snapshot.Days.FirstOrDefault();
            if (today != null)
            {
                string range = "Today  " + Units.FormatTemperature(today.HighF)
                               + " / " + Units.FormatTemperature(today.LowF);
                Theme.DrawText(g, range, Theme.FontBodyBold, Theme.Text,
                    new Rectangle(textX + 44, inner.Y + 90, inner.Width - textX - 40, 22), wrap: false);
            }

            string stamp = "Updated " + Snapshot.RetrievedAt.ToString("h:mm tt")
                           + (string.IsNullOrEmpty(current.Source) ? string.Empty : "  ·  " + current.Source);
            Theme.DrawText(g, stamp, Theme.FontSmall, Theme.TextFaint,
                new Rectangle(inner.X, inner.Bottom - 18, inner.Width, 18), wrap: false);
        }

        /// <summary>
        /// Draws the next 24 hours: precipitation probability as bars, temperature
        /// as a line over the top, with hours expecting snow or thunder marked.
        /// </summary>
        private void DrawHourlyChart(Graphics g, Rectangle bounds)
        {
            Theme.DrawCard(g, bounds);

            var inner = Rectangle.Inflate(bounds, -16, -12);
            Theme.DrawText(g, "NEXT 24 HOURS", Theme.FontSmallBold, Theme.TextMuted,
                new Rectangle(inner.X, inner.Y, inner.Width, 16), wrap: false);

            List<HourlyPoint> hours = UpcomingHours(24);
            if (hours.Count < 2)
            {
                Theme.DrawText(g, "Hourly data is unavailable.", Theme.FontBody, Theme.TextMuted,
                    new Rectangle(inner.X, inner.Y + 30, inner.Width, 20));
                return;
            }

            var plot = new Rectangle(inner.X, inner.Y + 22, inner.Width, inner.Height - 44);
            if (plot.Width <= 10 || plot.Height <= 20) return;

            double columnWidth = (double)plot.Width / hours.Count;

            // Probability bars occupy the lower 40% so the temperature line stays readable.
            int barZoneTop = plot.Y + (int)(plot.Height * 0.60);
            int barZoneHeight = plot.Bottom - barZoneTop;

            for (int i = 0; i < hours.Count; i++)
            {
                HourlyPoint hour = hours[i];
                double probability = hour.PrecipitationProbability ?? 0d;
                if (probability <= 0) continue;

                int barHeight = (int)Math.Round(barZoneHeight * Math.Min(probability, 100d) / 100d);
                if (barHeight < 2) barHeight = 2;

                var bar = new Rectangle(
                    (int)(plot.X + i * columnWidth) + 1,
                    plot.Bottom - barHeight,
                    Math.Max(2, (int)columnWidth - 2),
                    barHeight);

                Color color = Theme.Rain;
                if ((hour.SnowfallInches ?? 0) > 0.01 || WeatherCodes.IsSnow(hour.WeatherCode)) color = Theme.Snow;
                else if (WeatherCodes.IsFreezing(hour.WeatherCode)) color = Theme.Ice;
                else if (hour.IsThunder) color = Theme.Thunder;

                using (var brush = new SolidBrush(Color.FromArgb(190, color)))
                {
                    g.FillRectangle(brush, bar);
                }
            }

            DrawTemperatureLine(g, plot, hours, columnWidth, barZoneTop);
            DrawHourLabels(g, plot, hours, columnWidth);
        }

        private static void DrawTemperatureLine(
            Graphics g, Rectangle plot, List<HourlyPoint> hours, double columnWidth, int lineZoneBottom)
        {
            var temperatures = hours
                .Select(h => h.TemperatureF)
                .Where(t => t.HasValue)
                .Select(t => t.Value)
                .ToList();

            if (temperatures.Count < 2) return;

            double min = temperatures.Min();
            double max = temperatures.Max();
            double span = Math.Max(1d, max - min);

            int lineZoneTop = plot.Y + 14;
            int lineZoneHeight = Math.Max(10, lineZoneBottom - lineZoneTop - 6);

            var points = new List<PointF>();
            for (int i = 0; i < hours.Count; i++)
            {
                if (!hours[i].TemperatureF.HasValue) continue;

                float x = (float)(plot.X + i * columnWidth + columnWidth / 2d);
                float y = (float)(lineZoneTop + lineZoneHeight * (1d - (hours[i].TemperatureF.Value - min) / span));
                points.Add(new PointF(x, y));
            }

            if (points.Count < 2) return;

            using (var pen = new Pen(Theme.Accent, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, points.ToArray());
            }

            // Label only the extremes; labelling every hour is unreadable at this size.
            int hottest = temperatures.IndexOf(max);
            int coldest = temperatures.IndexOf(min);

            LabelPoint(g, points, hottest, max);
            if (coldest != hottest) LabelPoint(g, points, coldest, min);
        }

        private static void LabelPoint(Graphics g, List<PointF> points, int index, double value)
        {
            if (index < 0 || index >= points.Count) return;

            PointF point = points[index];
            var bounds = new Rectangle((int)point.X - 24, (int)point.Y - 22, 48, 18);

            Theme.DrawText(g, Units.FormatTemperature(value), Theme.FontSmallBold, Theme.Text,
                bounds, StringAlignment.Center, StringAlignment.Center, false);
        }

        private static void DrawHourLabels(
            Graphics g, Rectangle plot, List<HourlyPoint> hours, double columnWidth)
        {
            // A label every three hours keeps the axis legible at any window width.
            int step = Math.Max(1, (int)Math.Ceiling(52d / Math.Max(1d, columnWidth)));

            for (int i = 0; i < hours.Count; i += step)
            {
                var bounds = new Rectangle(
                    (int)(plot.X + i * columnWidth) - 14,
                    plot.Bottom + 2,
                    (int)columnWidth + 28,
                    16);

                Theme.DrawText(g, hours[i].Time.ToString("htt").ToLowerInvariant(),
                    Theme.FontSmall, Theme.TextFaint, bounds,
                    StringAlignment.Center, StringAlignment.Center, false);
            }
        }

        private void DrawDetails(Graphics g, Rectangle bounds)
        {
            if (bounds.Height < 60) return;

            Theme.DrawCard(g, bounds);
            var inner = Rectangle.Inflate(bounds, -16, -12);

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

            int columns = Math.Max(2, Math.Min(4, inner.Width / 170));
            int columnWidth = inner.Width / columns;
            const int RowHeight = 40;

            for (int i = 0; i < entries.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                int x = inner.X + column * columnWidth;
                int y = inner.Y + row * RowHeight;
                if (y + RowHeight > inner.Bottom) break;

                Theme.DrawText(g, entries[i].Key.ToUpperInvariant(), Theme.FontSmall, Theme.TextFaint,
                    new Rectangle(x, y, columnWidth - 10, 15), wrap: false);
                Theme.DrawText(g, entries[i].Value, Theme.FontBodyBold, Theme.Text,
                    new Rectangle(x, y + 15, columnWidth - 10, 20), wrap: false);
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
