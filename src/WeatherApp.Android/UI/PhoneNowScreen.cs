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
    /// Current conditions, the next 24 hours, today's precipitation split by
    /// type, and the supporting numbers -- as one scrolling column.
    ///
    /// The desktop version of this is three panels side by side. On a phone the
    /// same information has to be stacked, which is why this is a scrolling screen
    /// rather than a port of the desktop layout.
    /// </summary>
    public sealed class PhoneNowScreen : PhoneScreen
    {
        private const double Gutter = 14;
        private const double HeaderHeight = 186;
        private const double ChartHeight = 196;
        private const double PrecipRowHeight = 34;
        private const double DetailRowHeight = 46;

        protected override void OnSnapshotChanged()
        {
            ResetScroll();
        }

        protected override double ContentHeight
        {
            get
            {
                if (Snapshot == null) return H;

                double height = Gutter + HeaderHeight + Gutter + ChartHeight + Gutter;
                height += PrecipCardHeight() + Gutter;
                height += DetailCardHeight() + Gutter;
                return height;
            }
        }

        protected override void DrawContent(DrawingContext context, double width)
        {
            if (Snapshot == null)
            {
                DrawPlaceholder(context, "Loading conditions...");
                return;
            }

            double inner = width - Gutter * 2;
            if (inner < 60) return;

            double y = Gutter;

            DrawHeader(context, new Rect(Gutter, y, inner, HeaderHeight));
            y += HeaderHeight + Gutter;

            DrawHourly(context, new Rect(Gutter, y, inner, ChartHeight));
            y += ChartHeight + Gutter;

            DrawPrecipitation(context, new Rect(Gutter, y, inner, PrecipCardHeight()));
            y += PrecipCardHeight() + Gutter;

            DrawDetails(context, new Rect(Gutter, y, inner, DetailCardHeight()));
        }

        private void DrawHeader(DrawingContext context, Rect bounds)
        {
            AppTheme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(16, 14));

            CurrentConditions current = Snapshot.Current;

            AppTheme.DrawLineText(context,
                Snapshot.Location != null ? Snapshot.Location.DisplayName : "Unknown location",
                AppTheme.Bold, AppTheme.SizeSmall, AppTheme.TextMuted,
                new Rect(inner.X, inner.Y, inner.Width, 18));

            if (current == null)
            {
                AppTheme.DrawText(context, "Current conditions are unavailable.", AppTheme.Regular,
                    AppTheme.SizeBody, AppTheme.TextMuted,
                    new Rect(inner.X, inner.Y + 28, inner.Width, 40));
                return;
            }

            // Temperature and glyph on one line, then the wording under them.
            AppTheme.DrawLineText(context, Units.FormatTemperature(current.TemperatureF),
                AppTheme.Regular, 54, AppTheme.Text,
                new Rect(inner.X, inner.Y + 24, 160, 66));

            AppTheme.DrawLineText(context, WeatherCodes.Glyph(current.WeatherCode, current.IsDaytime),
                AppTheme.Regular, AppTheme.SizeGlyph, AppTheme.Accent,
                new Rect(inner.X + 150, inner.Y + 32, 46, 50), TextAlignment.Center);

            AppTheme.DrawLineText(context, current.Summary ?? "--", AppTheme.Bold,
                AppTheme.SizeHeading, AppTheme.Text,
                new Rect(inner.X, inner.Y + 94, inner.Width, 24));

            var extras = new List<string>();
            if (current.FeelsLikeF.HasValue)
            {
                extras.Add("Feels " + Units.FormatTemperature(current.FeelsLikeF));
            }
            if (current.WindSpeedMph.HasValue)
            {
                extras.Add(current.WindDirectionCardinal + " " + Units.FormatSpeed(current.WindSpeedMph));
            }

            ForecastDay today = Snapshot.Days.FirstOrDefault();
            if (today != null)
            {
                extras.Add(Units.FormatTemperature(today.HighF) + " / " + Units.FormatTemperature(today.LowF));
            }

            AppTheme.DrawLineText(context, string.Join("   ·   ", extras), AppTheme.Regular,
                AppTheme.SizeBody, AppTheme.TextMuted,
                new Rect(inner.X, inner.Y + 120, inner.Width, 22));

            AppTheme.DrawLineText(context, "Updated " + Snapshot.RetrievedAt.ToString("h:mm tt"),
                AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextFaint,
                new Rect(inner.X, inner.Bottom - 18, inner.Width, 18));
        }

        private void DrawHourly(DrawingContext context, Rect bounds)
        {
            AppTheme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(14, 12));

            AppTheme.DrawLineText(context, "NEXT 24 HOURS", AppTheme.Bold, AppTheme.SizeSmall,
                AppTheme.TextMuted, new Rect(inner.X, inner.Y, inner.Width, 16), middle: false);

            List<HourlyPoint> hours = HourlyChartRenderer.Upcoming(Snapshot, 24);
            if (hours.Count < 2)
            {
                AppTheme.DrawText(context, "Hourly data is unavailable.", AppTheme.Regular,
                    AppTheme.SizeBody, AppTheme.TextMuted,
                    new Rect(inner.X, inner.Y + 28, inner.Width, 20));
                return;
            }

            HourlyChartRenderer.Draw(context,
                new Rect(inner.X, inner.Y + 24, inner.Width, inner.Height - 46), hours);
        }

        // ---- today's precipitation, by type ----------------------------------

        private PrecipitationDay Today
        {
            get
            {
                return Snapshot == null
                    ? null
                    : Snapshot.Precipitation.Days.FirstOrDefault();
            }
        }

        private double PrecipCardHeight()
        {
            return Today == null ? 64 : 44 + PrecipRowHeight * 5 + 12;
        }

        /// <summary>
        /// The precipitation table from the desktop head, turned on its side.
        /// Nine columns will not fit a phone, but five rows will, and the
        /// distinction between rain and snow is the whole point of the tab.
        /// </summary>
        private void DrawPrecipitation(DrawingContext context, Rect bounds)
        {
            AppTheme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(14, 12));

            AppTheme.DrawLineText(context, "TODAY, BY TYPE", AppTheme.Bold, AppTheme.SizeSmall,
                AppTheme.TextMuted, new Rect(inner.X, inner.Y, inner.Width, 16), middle: false);

            PrecipitationDay day = Today;
            if (day == null)
            {
                AppTheme.DrawText(context, "No precipitation outlook available.", AppTheme.Regular,
                    AppTheme.SizeBody, AppTheme.TextMuted,
                    new Rect(inner.X, inner.Y + 26, inner.Width, 20));
                return;
            }

            var rows = new[]
            {
                Tuple.Create("Rain", day.RainChance, AppTheme.RainColor),
                Tuple.Create("Snow", day.SnowChance, AppTheme.SnowColor),
                Tuple.Create("Freezing rain", day.IcyChance, AppTheme.IceColor),
                Tuple.Create("Thunderstorms", day.ThunderstormChance, AppTheme.ThunderColor),
                Tuple.Create("Hail (SPC severe)", day.SevereHailChance, AppTheme.HailColor)
            };

            double y = inner.Y + 26;
            foreach (var row in rows)
            {
                AppTheme.DrawLineText(context, row.Item1, AppTheme.Regular, AppTheme.SizeBody,
                    AppTheme.Text, new Rect(inner.X, y, inner.Width - 90, PrecipRowHeight));

                // A dash means the value was never issued, not that the chance is
                // zero -- SPC hail only reaches three days out.
                string text = row.Item2.HasValue
                    ? (row.Item2.Value < 1 ? "<1%" : Math.Round(row.Item2.Value).ToString("0") + "%")
                    : "--";

                var pill = new Rect(inner.Right - 68, y + PrecipRowHeight / 2 - 12, 68, 24);
                Color tint = row.Item2.HasValue ? row.Item3 : AppTheme.TextFaintColor;

                if (row.Item2.HasValue)
                {
                    context.DrawRectangle(AppTheme.Brush(tint, 46),
                        new Pen(AppTheme.Brush(tint, 120)), pill, 12, 12);
                }

                AppTheme.DrawLineText(context, text, AppTheme.Bold, AppTheme.SizeBody,
                    AppTheme.Brush(tint), pill, TextAlignment.Center);

                y += PrecipRowHeight;
            }
        }

        // ---- supporting numbers ----------------------------------------------

        private List<KeyValuePair<string, string>> DetailEntries()
        {
            var entries = new List<KeyValuePair<string, string>>();
            if (Snapshot == null) return entries;

            CurrentConditions current = Snapshot.Current;
            if (current != null)
            {
                entries.Add(new KeyValuePair<string, string>("Humidity", Units.FormatPercent(current.RelativeHumidity)));
                entries.Add(new KeyValuePair<string, string>("Pressure", Units.FormatPressure(current.PressureMb)));
                entries.Add(new KeyValuePair<string, string>("Cloud cover", Units.FormatPercent(current.CloudCoverPercent)));
                entries.Add(new KeyValuePair<string, string>("Wind gust", Units.FormatSpeed(current.WindGustMph)));
            }

            ForecastDay today = Snapshot.Days.FirstOrDefault();
            if (today != null)
            {
                if (today.Sunrise.HasValue)
                {
                    entries.Add(new KeyValuePair<string, string>("Sunrise", today.Sunrise.Value.ToString("h:mm tt")));
                }
                if (today.Sunset.HasValue)
                {
                    entries.Add(new KeyValuePair<string, string>("Sunset", today.Sunset.Value.ToString("h:mm tt")));
                }
            }

            return entries;
        }

        private double DetailCardHeight()
        {
            int rows = (DetailEntries().Count + 1) / 2;
            return rows == 0 ? 0 : 20 + rows * DetailRowHeight;
        }

        private void DrawDetails(DrawingContext context, Rect bounds)
        {
            if (bounds.Height < 40) return;

            AppTheme.DrawCard(context, bounds);
            Rect inner = bounds.Deflate(new Thickness(16, 10));

            List<KeyValuePair<string, string>> entries = DetailEntries();
            double columnWidth = inner.Width / 2;

            for (int i = 0; i < entries.Count; i++)
            {
                double x = inner.X + (i % 2) * columnWidth;
                double y = inner.Y + (i / 2) * DetailRowHeight;

                AppTheme.DrawLineText(context, entries[i].Key.ToUpperInvariant(), AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint,
                    new Rect(x, y, columnWidth - 10, 16));
                AppTheme.DrawLineText(context, entries[i].Value, AppTheme.Bold, AppTheme.SizeBody,
                    AppTheme.Text, new Rect(x, y + 18, columnWidth - 10, 22));
            }
        }
    }
}
