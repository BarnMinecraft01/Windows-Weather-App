using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Precipitation broken out by type: rain, snow, freezing rain, thunderstorms
    /// and hail, day by day.
    ///
    /// A dash means the value was never issued, not that the chance is zero. SPC
    /// hail probabilities stop at Day 3, so the later rows genuinely have nothing
    /// to show there and must not imply "no hail".
    /// </summary>
    public sealed class PrecipitationPanel : WeatherPanel
    {
        private const double Gutter = 16;
        private const double HeaderRowHeight = 28;
        private const double RowHeight = 40;
        private const double NotesHeight = 66;

        private sealed class Column
        {
            public string Title;
            public double Width;
            public Func<PrecipitationDay, string> Value;
            public Func<PrecipitationDay, Color> Tint;
        }

        private readonly List<Column> _columns;

        public PrecipitationPanel()
        {
            _columns = new List<Column>
            {
                new Column { Title = "DAY", Width = 124,
                    Value = d => d.DayLabel, Tint = d => AppTheme.TextColor },
                new Column { Title = "ANY PRECIP", Width = 104,
                    Value = d => Percent(d.AnyPrecipitationChance),
                    Tint = d => Shade(d.AnyPrecipitationChance, AppTheme.AccentColor) },
                new Column { Title = "RAIN", Width = 84,
                    Value = d => Percent(d.RainChance),
                    Tint = d => Shade(d.RainChance, AppTheme.RainColor) },
                new Column { Title = "SNOW", Width = 84,
                    Value = d => Percent(d.SnowChance),
                    Tint = d => Shade(d.SnowChance, AppTheme.SnowColor) },
                new Column { Title = "FRZ RAIN", Width = 92,
                    Value = d => Percent(d.IcyChance),
                    Tint = d => Shade(d.IcyChance, AppTheme.IceColor) },
                new Column { Title = "T-STORM", Width = 92,
                    Value = d => Percent(d.ThunderstormChance),
                    Tint = d => Shade(d.ThunderstormChance, AppTheme.ThunderColor) },
                new Column { Title = "HAIL (SPC)", Width = 100,
                    Value = d => Percent(d.SevereHailChance),
                    Tint = d => Shade(d.SevereHailChance, AppTheme.HailColor) },
                new Column { Title = "SEVERE RISK", Width = 140,
                    Value = d => string.IsNullOrEmpty(d.SevereRiskCategory) ? "--" : d.SevereRiskCategory,
                    Tint = d => RiskColor(d.SevereRiskCategory) },
                new Column { Title = "AMOUNT", Width = 112,
                    Value = FormatAmount, Tint = d => AppTheme.TextMutedColor }
            };
        }

        protected override void DrawSurface(DrawingContext context)
        {
            if (Snapshot == null || Snapshot.Precipitation.Days.Count == 0)
            {
                DrawPlaceholder(context, "No precipitation outlook available.");
                return;
            }

            var table = new Rect(Gutter, Gutter,
                Math.Max(10, W - Gutter * 2),
                Math.Max(80, H - NotesHeight - Gutter * 3));

            AppTheme.DrawCard(context, table);
            DrawTable(context, table);
            DrawNotes(context);
        }

        private void DrawTable(DrawingContext context, Rect bounds)
        {
            Rect inner = bounds.Deflate(new Thickness(14, 10));

            // Spread any spare width across the columns so the table fills the
            // window rather than leaving a gap on the right.
            double fixedWidth = _columns.Sum(c => c.Width);
            double extra = Math.Max(0, inner.Width - fixedWidth) / _columns.Count;

            double x = inner.X;
            foreach (Column column in _columns)
            {
                double width = column.Width + extra;
                AppTheme.DrawLineText(context, column.Title, AppTheme.Bold, AppTheme.SizeSmall, AppTheme.TextFaint,
                    new Rect(x, inner.Y, width - 10, HeaderRowHeight));
                x += width;
            }

            double lineY = inner.Y + HeaderRowHeight;
            context.DrawLine(AppTheme.BorderPen, new Point(inner.X, lineY), new Point(inner.Right, lineY));

            List<PrecipitationDay> days = Snapshot.Precipitation.Days;
            double y = inner.Y + HeaderRowHeight + 3;

            for (int i = 0; i < days.Count; i++)
            {
                if (y + RowHeight > inner.Bottom) break;

                PrecipitationDay day = days[i];

                if (i % 2 == 1)
                {
                    context.DrawRectangle(AppTheme.Brush(AppTheme.SurfaceAlt, 40), null,
                        new Rect(inner.X, y, inner.Width, RowHeight));
                }

                x = inner.X;
                for (int c = 0; c < _columns.Count; c++)
                {
                    Column column = _columns[c];
                    double width = column.Width + extra;
                    var cell = new Rect(x, y, width - 10, RowHeight);

                    string text = column.Value(day);
                    Color color = column.Tint(day);

                    // The probability columns get a pill, which makes the high-risk
                    // cells findable at a glance rather than by reading every number.
                    if (c >= 1 && c <= 6 && text != "--")
                    {
                        DrawProbabilityPill(context, cell, text, color);
                    }
                    else
                    {
                        AppTheme.DrawLineText(context, text, c == 0 ? AppTheme.Bold : AppTheme.Regular,
                            AppTheme.SizeBody, AppTheme.Brush(color), cell);
                    }

                    x += width;
                }

                y += RowHeight;
            }
        }

        private static void DrawProbabilityPill(DrawingContext context, Rect cell, string text, Color color)
        {
            var pill = new Rect(cell.X, cell.Y + cell.Height / 2 - 11, Math.Min(64, cell.Width), 22);

            context.DrawRectangle(AppTheme.Brush(color, 46), new Pen(AppTheme.Brush(color, 120)), pill, 11, 11);
            AppTheme.DrawLineText(context, text, AppTheme.Bold, AppTheme.SizeBody, AppTheme.Brush(color),
                pill, TextAlignment.Center);
        }

        private void DrawNotes(DrawingContext context)
        {
            var bounds = new Rect(Gutter, H - NotesHeight - Gutter, Math.Max(10, W - Gutter * 2), NotesHeight);
            if (bounds.Y < Gutter) return;

            var notes = new List<string>(Snapshot.Precipitation.Notes);

            // Surface CAPE only when it means something; below about 500 J/kg it is
            // not a useful storm signal and would just be noise.
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

            AppTheme.DrawText(context, string.Join("  ", notes), AppTheme.Regular, AppTheme.SizeSmall,
                AppTheme.TextFaint, bounds);
        }

        // ---- formatting ------------------------------------------------------

        private static string Percent(double? value)
        {
            if (!value.HasValue) return "--";
            if (value.Value < 1) return "<1%";
            return Math.Round(value.Value).ToString("0") + "%";
        }

        private static Color Shade(double? value, Color baseColor)
        {
            if (!value.HasValue) return AppTheme.TextFaintColor;
            if (value.Value < 10) return Color.FromArgb(150, baseColor.R, baseColor.G, baseColor.B);
            return baseColor;
        }

        private static Color RiskColor(string category)
        {
            switch ((category ?? string.Empty).ToLowerInvariant())
            {
                case "high":
                case "moderate": return AppTheme.DangerColor;
                case "enhanced": return AppTheme.WarningColor;
                case "slight": return AppTheme.CautionColor;
                case "marginal": return AppTheme.OkColor;
                case "general thunderstorms": return AppTheme.TextMutedColor;
                default: return AppTheme.TextFaintColor;
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
