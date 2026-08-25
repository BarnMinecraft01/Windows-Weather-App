using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Active watches, warnings and advisories: a list that opens into the full
    /// product text.
    ///
    /// The desktop head shows list and detail side by side. A phone has room for
    /// one at a time, so tapping a row replaces the list and a back row returns.
    /// The full text is kept rather than summarised -- the call-to-action
    /// paragraph is the part that tells you what to do, and it is exactly what
    /// gets dropped when an app decides a warning is too long for a phone.
    /// </summary>
    public sealed class PhoneAlertsScreen : PhoneScreen
    {
        private const double Margin = 14;
        private const double RowHeight = 78;
        private const double BackRowHeight = 44;

        private readonly List<WeatherAlert> _alerts = new List<WeatherAlert>();
        private int _openIndex = -1;
        private double _detailHeight = 600;

        protected override void OnSnapshotChanged()
        {
            _alerts.Clear();
            _openIndex = -1;
            ResetScroll();

            if (Snapshot != null) _alerts.AddRange(Snapshot.Alerts.Alerts);
        }

        protected override double ContentHeight
        {
            get
            {
                if (_openIndex >= 0) return BackRowHeight + _detailHeight + Margin * 2;
                return Margin * 2 + 34 + Math.Max(1, _alerts.Count) * RowHeight;
            }
        }

        protected override void OnTap(Point point)
        {
            if (_openIndex >= 0)
            {
                // Anything in the back row returns to the list.
                if (point.Y <= Margin + BackRowHeight)
                {
                    _openIndex = -1;
                    ResetScroll();
                    InvalidateSurface();
                }
                return;
            }

            double y = Margin + 34;
            for (int i = 0; i < _alerts.Count; i++)
            {
                if (point.Y >= y && point.Y < y + RowHeight)
                {
                    _openIndex = i;
                    ResetScroll();
                    InvalidateSurface();
                    return;
                }
                y += RowHeight;
            }
        }

        protected override void DrawContent(DrawingContext context, double width)
        {
            double inner = width - Margin * 2;
            if (inner < 60) return;

            if (_openIndex >= 0 && _openIndex < _alerts.Count)
            {
                DrawDetail(context, inner);
                return;
            }

            DrawSummary(context, inner);

            if (_alerts.Count == 0)
            {
                AppTheme.DrawText(context,
                    "There are no active watches, warnings or advisories for this area.",
                    AppTheme.Regular, AppTheme.SizeBody, AppTheme.TextMuted,
                    new Rect(Margin, Margin + 44, inner, 60));
                return;
            }

            double y = Margin + 34;
            for (int i = 0; i < _alerts.Count; i++)
            {
                DrawRow(context, new Rect(Margin, y, inner, RowHeight - 8), _alerts[i]);
                y += RowHeight;
            }
        }

        private void DrawSummary(DrawingContext context, double inner)
        {
            string scope = Snapshot == null || string.IsNullOrEmpty(Snapshot.Alerts.ScopeDescription)
                ? "the selected area"
                : Snapshot.Alerts.ScopeDescription;

            if (_alerts.Count == 0)
            {
                AppTheme.DrawLineText(context, "No active alerts for " + scope + ".", AppTheme.Bold,
                    AppTheme.SizeBody, AppTheme.Brush(AppTheme.OkColor),
                    new Rect(Margin, Margin, inner, 26));
                return;
            }

            int warnings = 0;
            foreach (WeatherAlert alert in _alerts)
            {
                if (alert.IsWarning) warnings++;
            }

            var text = new StringBuilder();
            text.Append(_alerts.Count).Append(_alerts.Count == 1 ? " active alert" : " active alerts");
            if (warnings > 0)
            {
                text.Append("  ·  ").Append(warnings).Append(warnings == 1 ? " warning" : " warnings");
            }

            AppTheme.DrawLineText(context, text.ToString(), AppTheme.Bold, AppTheme.SizeBody,
                AppTheme.Brush(warnings > 0 ? AppTheme.DangerColor : AppTheme.WarningColor),
                new Rect(Margin, Margin, inner, 26));
        }

        private static void DrawRow(DrawingContext context, Rect bounds, WeatherAlert alert)
        {
            AppTheme.DrawCard(context, bounds);

            Color accent = AppTheme.AlertColor(alert.Severity, alert.IsWarning);
            context.DrawRectangle(AppTheme.Brush(accent), null,
                new Rect(bounds.X, bounds.Y + 6, 4, bounds.Height - 12), 2, 2);

            Rect inner = bounds.Deflate(new Thickness(16, 8));

            AppTheme.DrawLineText(context, alert.Event ?? "Weather Alert", AppTheme.Bold,
                AppTheme.SizeBody, AppTheme.Brush(accent),
                new Rect(inner.X, inner.Y, inner.Width - 14, 22), middle: false);

            string when = alert.Expires.HasValue
                ? "Until " + alert.Expires.Value.ToString("ddd h:mm tt")
                : string.Empty;

            AppTheme.DrawLineText(context, when, AppTheme.Regular, AppTheme.SizeSmall,
                AppTheme.TextMuted, new Rect(inner.X, inner.Y + 22, inner.Width - 14, 18), middle: false);

            AppTheme.DrawLineText(context, alert.AreaDescription ?? string.Empty, AppTheme.Regular,
                AppTheme.SizeSmall, AppTheme.TextFaint,
                new Rect(inner.X, inner.Y + 40, inner.Width - 14, 18), middle: false);

            AppTheme.DrawLineText(context, "›", AppTheme.Regular, AppTheme.SizeHeading,
                AppTheme.TextFaint, new Rect(inner.Right - 12, inner.Y + 10, 12, 24),
                TextAlignment.Center);
        }

        private void DrawDetail(DrawingContext context, double inner)
        {
            WeatherAlert alert = _alerts[_openIndex];
            Color accent = AppTheme.AlertColor(alert.Severity, alert.IsWarning);

            var back = new Rect(Margin, Margin, inner, BackRowHeight - 8);
            AppTheme.DrawCard(context, back, AppTheme.SurfaceAltBrush);
            AppTheme.DrawLineText(context, "‹  All alerts", AppTheme.Bold, AppTheme.SizeBody,
                AppTheme.Accent, back.Deflate(new Thickness(14, 0)));

            double y = Margin + BackRowHeight;

            AppTheme.DrawText(context, alert.Event ?? "Weather Alert", AppTheme.Bold,
                AppTheme.SizeHeading, AppTheme.Brush(accent), new Rect(Margin, y, inner, 30));
            y += 34;

            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(alert.Severity)) meta.Add(alert.Severity);
            if (!string.IsNullOrWhiteSpace(alert.Urgency)) meta.Add(alert.Urgency);
            if (alert.Expires.HasValue)
            {
                meta.Add("until " + alert.Expires.Value.ToString("ddd d MMM, h:mm tt"));
            }

            AppTheme.DrawText(context, string.Join("  ·  ", meta), AppTheme.Regular,
                AppTheme.SizeSmall, AppTheme.TextMuted, new Rect(Margin, y, inner, 20));
            y += 24;

            if (!string.IsNullOrWhiteSpace(alert.AreaDescription))
            {
                AppTheme.DrawText(context, alert.AreaDescription, AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(Margin, y, inner, 44));
                y += 48;
            }

            var body = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(alert.Headline))
            {
                body.AppendLine(alert.Headline).AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(alert.Description))
            {
                body.AppendLine(Rewrap(alert.Description)).AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(alert.Instruction))
            {
                body.AppendLine("WHAT TO DO").AppendLine(Rewrap(alert.Instruction));
            }

            string text = body.ToString();

            // Estimate the wrapped height so the screen knows how far it scrolls.
            // Measured rather than guessed, because warning text varies enormously.
            Size measured = AppTheme.MeasureWrapped(text, AppTheme.Regular, AppTheme.SizeBody, inner);
            _detailHeight = y + measured.Height + Margin;

            AppTheme.DrawText(context, text, AppTheme.Regular, AppTheme.SizeBody, AppTheme.Text,
                new Rect(Margin, y, inner, measured.Height + 8));
        }

        /// <summary>
        /// NWS product text arrives hard-wrapped to the old teletype width, which
        /// looks ragged on a phone. Single newlines become spaces; blank lines stay
        /// as paragraph breaks.
        /// </summary>
        private static string Rewrap(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string[] paragraphs = text.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None);
            var result = new StringBuilder();

            foreach (string raw in paragraphs)
            {
                string paragraph = raw.Replace('\n', ' ').Trim();
                while (paragraph.Contains("  ")) paragraph = paragraph.Replace("  ", " ");
                if (paragraph.Length == 0) continue;

                if (result.Length > 0) result.AppendLine().AppendLine();
                result.Append(paragraph);
            }

            return result.ToString();
        }
    }
}
