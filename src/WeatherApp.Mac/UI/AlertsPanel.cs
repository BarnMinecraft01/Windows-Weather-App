using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Active NWS watches, warnings and advisories.
    ///
    /// Sorted so the things that can hurt you are at the top, and showing the full
    /// product text rather than a summary line -- the call-to-action paragraph at
    /// the bottom of a warning is the part that tells you what to do, and it is
    /// the first thing summarising apps throw away.
    ///
    /// The list is drawn by hand rather than being a ListBox so the severity
    /// colour bar and two-line rows match the rest of the app exactly.
    /// </summary>
    public sealed class AlertsPanel : WeatherPanel
    {
        private const double Gutter = 16;
        private const double SummaryHeight = 24;
        private const double RowHeight = 50;

        private readonly TextBox _detail;
        private readonly List<WeatherAlert> _alerts = new List<WeatherAlert>();

        private int _selectedIndex;
        private int _hoverIndex = -1;
        private double _scrollOffset;

        public AlertsPanel()
        {
            _detail = Widgets.ReadOnlyText();
            _detail.Text = "No alerts loaded.";
            Children.Add(_detail);

            PointerPressed += (s, e) =>
            {
                int index = IndexAt(e.GetPosition(this));
                if (index < 0 || index == _selectedIndex) return;

                _selectedIndex = index;
                ShowDetail();
                InvalidateSurface();
            };

            PointerMoved += (s, e) =>
            {
                int index = IndexAt(e.GetPosition(this));
                if (index == _hoverIndex) return;

                _hoverIndex = index;
                InvalidateSurface();
            };

            PointerExited += (s, e) =>
            {
                if (_hoverIndex == -1) return;

                _hoverIndex = -1;
                InvalidateSurface();
            };

            // The list can outrun its column when a whole region is in play.
            PointerWheelChanged += (s, e) =>
            {
                if (!ListBounds().Contains(e.GetPosition(this))) return;

                double maxOffset = Math.Max(0, _alerts.Count * RowHeight - ListBounds().Height);
                _scrollOffset = Math.Min(maxOffset, Math.Max(0, _scrollOffset - e.Delta.Y * RowHeight));
                InvalidateSurface();
            };
        }

        protected override void OnSnapshotChanged()
        {
            _alerts.Clear();
            _selectedIndex = 0;
            _hoverIndex = -1;
            _scrollOffset = 0;

            if (Snapshot != null) _alerts.AddRange(Snapshot.Alerts.Alerts);

            if (_alerts.Count > 0)
            {
                ShowDetail();
            }
            else
            {
                _detail.Text =
                    "There are no active watches, warnings or advisories for this area."
                    + Environment.NewLine + Environment.NewLine
                    + "This panel refreshes with the rest of the app. Alerts are issued by the "
                    + "National Weather Service and appear here within a couple of minutes of publication.";
            }
        }

        protected override void LayoutChildren()
        {
            Rect list = ListBounds();
            double left = list.Right + Gutter;
            Place(_detail, left, list.Y, Math.Max(60, W - left - Gutter), list.Height);
        }

        protected override void DrawSurface(DrawingContext context)
        {
            DrawSummary(context);

            Rect list = ListBounds();
            Theme.DrawCard(context, list);

            if (_alerts.Count == 0) return;

            using (context.PushClip(list))
            {
                for (int i = 0; i < _alerts.Count; i++)
                {
                    double top = list.Y + 4 + i * RowHeight - _scrollOffset;
                    if (top + RowHeight < list.Y) continue;
                    if (top > list.Bottom) break;

                    DrawRow(context, new Rect(list.X + 2, top, list.Width - 4, RowHeight), _alerts[i], i);
                }
            }
        }

        private void DrawSummary(DrawingContext context)
        {
            var bounds = new Rect(Gutter, Gutter - 4, Math.Max(10, W - Gutter * 2), SummaryHeight);

            if (Snapshot == null)
            {
                Theme.DrawLineText(context, "No alerts loaded.", Theme.Bold, Theme.SizeBody,
                    Theme.TextMuted, bounds);
                return;
            }

            string scope = string.IsNullOrEmpty(Snapshot.Alerts.ScopeDescription)
                ? "the selected area"
                : Snapshot.Alerts.ScopeDescription;

            if (_alerts.Count == 0)
            {
                Theme.DrawLineText(context, "No active alerts for " + scope + ".", Theme.Bold,
                    Theme.SizeBody, Theme.Brush(Theme.OkColor), bounds);
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
                text.Append(" (").Append(warnings).Append(warnings == 1 ? " warning" : " warnings").Append(')');
            }
            text.Append(" for ").Append(scope).Append('.');

            Theme.DrawLineText(context, text.ToString(), Theme.Bold, Theme.SizeBody,
                Theme.Brush(warnings > 0 ? Theme.DangerColor : Theme.WarningColor), bounds);
        }

        private void DrawRow(DrawingContext context, Rect bounds, WeatherAlert alert, int index)
        {
            if (index == _selectedIndex)
            {
                context.DrawRectangle(Theme.SurfaceAltBrush, null, bounds);
            }
            else if (index == _hoverIndex)
            {
                context.DrawRectangle(Theme.Brush(Theme.SurfaceHover, 70), null, bounds);
            }

            Color accent = Theme.AlertColor(alert.Severity, alert.IsWarning);

            // A colour bar down the left edge, so severity reads without depending
            // on the text colour alone.
            context.DrawRectangle(Theme.Brush(accent), null,
                new Rect(bounds.X, bounds.Y + 5, 4, bounds.Height - 10), 2, 2);

            Theme.DrawLineText(context, alert.Event ?? "Weather Alert", Theme.Bold, Theme.SizeBody,
                Theme.Brush(accent), new Rect(bounds.X + 14, bounds.Y + 6, bounds.Width - 20, 20));

            string subtitle = alert.AreaDescription ?? string.Empty;
            if (alert.Expires.HasValue)
            {
                subtitle = "Until " + alert.Expires.Value.ToString("ddd h:mm tt")
                           + (string.IsNullOrEmpty(subtitle) ? string.Empty : "  ·  " + subtitle);
            }

            Theme.DrawLineText(context, subtitle, Theme.Regular, Theme.SizeSmall, Theme.TextMuted,
                new Rect(bounds.X + 14, bounds.Y + 26, bounds.Width - 20, 18));
        }

        private void ShowDetail()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _alerts.Count) return;

            WeatherAlert alert = _alerts[_selectedIndex];
            var text = new StringBuilder();

            text.AppendLine(alert.Event ?? "Weather Alert");
            text.AppendLine(new string('=', Math.Max(8, (alert.Event ?? string.Empty).Length)));
            text.AppendLine();

            if (!string.IsNullOrWhiteSpace(alert.Headline))
            {
                text.AppendLine(alert.Headline);
                text.AppendLine();
            }

            AppendField(text, "Area", alert.AreaDescription);
            AppendField(text, "Severity", alert.Severity);
            AppendField(text, "Certainty", alert.Certainty);
            AppendField(text, "Urgency", alert.Urgency);

            if (alert.Effective.HasValue)
            {
                AppendField(text, "Effective", alert.Effective.Value.ToString("dddd d MMMM, h:mm tt"));
            }
            if (alert.Expires.HasValue)
            {
                AppendField(text, "Expires", alert.Expires.Value.ToString("dddd d MMMM, h:mm tt"));
            }
            AppendField(text, "Issued by", alert.SenderName);

            if (!string.IsNullOrWhiteSpace(alert.Description))
            {
                text.AppendLine();
                text.AppendLine(Rewrap(alert.Description));
            }

            if (!string.IsNullOrWhiteSpace(alert.Instruction))
            {
                text.AppendLine();
                text.AppendLine("WHAT TO DO");
                text.AppendLine(Rewrap(alert.Instruction));
            }

            _detail.Text = text.ToString();
            _detail.CaretIndex = 0;
        }

        private static void AppendField(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            builder.Append(label).Append(": ").AppendLine(value);
        }

        /// <summary>
        /// NWS product text arrives hard-wrapped to the old teletype width, which
        /// looks ragged in a resizable window. Single newlines become spaces while
        /// blank lines stay as paragraph breaks.
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

        private Rect ListBounds()
        {
            double top = Gutter + SummaryHeight;
            // A third of the width, bounded so the headline stays readable in a
            // narrow window and does not sprawl in a wide one.
            double width = Math.Max(240, Math.Min(420, (W - Gutter * 2) / 3));
            return new Rect(Gutter, top, width, Math.Max(60, H - top - Gutter));
        }

        private int IndexAt(Point position)
        {
            Rect list = ListBounds();
            if (!list.Contains(position)) return -1;

            double offset = position.Y - (list.Y + 4) + _scrollOffset;
            if (offset < 0) return -1;

            int index = (int)(offset / RowHeight);
            return index >= 0 && index < _alerts.Count ? index : -1;
        }
    }
}
