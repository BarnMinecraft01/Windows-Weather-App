using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Active NWS watches, warnings and advisories.
    ///
    /// Sorted so the things that can hurt you are at the top: tornado and flash
    /// flood emergencies first, then by severity, then by which expires soonest.
    /// The full product text is shown rather than a summary line, because the
    /// call-to-action paragraph at the bottom of a warning is the part that
    /// actually tells you what to do, and it is the first thing summarising apps
    /// throw away.
    /// </summary>
    public sealed class AlertsPanel : WeatherPanel
    {
        private readonly ListBox _list;
        private readonly TextBox _detail;
        private readonly Label _summaryLabel;
        private readonly List<WeatherAlert> _alerts = new List<WeatherAlert>();

        private const int Gutter = 14;
        private const int RowHeight = 46;

        public AlertsPanel()
        {
            _summaryLabel = new Label
            {
                Left = Gutter, Top = 12, Height = 20, AutoSize = false,
                Font = Theme.FontBodyBold, ForeColor = Theme.Text,
                Text = "No alerts loaded."
            };

            _list = new ListBox
            {
                Left = Gutter, Top = 40,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = RowHeight,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.FontBody,
                IntegralHeight = false
            };
            _list.DrawItem += DrawAlertRow;
            _list.SelectedIndexChanged += (s, e) => ShowDetail();

            _detail = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.FontBody,
                Top = 40
            };

            Controls.Add(_summaryLabel);
            Controls.Add(_list);
            Controls.Add(_detail);

            Resize += (s, e) => LayoutChildren();
        }

        protected override void OnSnapshotChanged()
        {
            _alerts.Clear();
            _list.BeginUpdate();
            _list.Items.Clear();

            if (Snapshot != null)
            {
                foreach (WeatherAlert alert in Snapshot.Alerts.Alerts)
                {
                    _alerts.Add(alert);
                    _list.Items.Add(alert.Event ?? "Weather Alert");
                }
            }

            _list.EndUpdate();

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
            else
            {
                _detail.Text = "There are no active watches, warnings or advisories for this area."
                    + Environment.NewLine + Environment.NewLine
                    + "This panel refreshes with the rest of the app. Alerts are issued by the "
                    + "National Weather Service and appear here within a couple of minutes of publication.";
            }

            UpdateSummary();
            LayoutChildren();
        }

        private void UpdateSummary()
        {
            if (Snapshot == null)
            {
                _summaryLabel.Text = "No alerts loaded.";
                _summaryLabel.ForeColor = Theme.TextMuted;
                return;
            }

            string scope = string.IsNullOrEmpty(Snapshot.Alerts.ScopeDescription)
                ? "the selected area"
                : Snapshot.Alerts.ScopeDescription;

            int warnings = 0;
            foreach (WeatherAlert alert in _alerts)
            {
                if (alert.IsWarning) warnings++;
            }

            if (_alerts.Count == 0)
            {
                _summaryLabel.Text = "No active alerts for " + scope + ".";
                _summaryLabel.ForeColor = Theme.Ok;
                return;
            }

            var text = new StringBuilder();
            text.Append(_alerts.Count).Append(_alerts.Count == 1 ? " active alert" : " active alerts");
            if (warnings > 0)
            {
                text.Append(" (").Append(warnings).Append(warnings == 1 ? " warning" : " warnings").Append(')');
            }
            text.Append(" for ").Append(scope).Append('.');

            _summaryLabel.Text = text.ToString();
            _summaryLabel.ForeColor = warnings > 0 ? Theme.Danger : Theme.Warning;
        }

        private void LayoutChildren()
        {
            int width = Math.Max(200, ClientSize.Width - Gutter * 2);
            _summaryLabel.Width = width;

            // Give the list a third of the width, with sensible bounds so the
            // headline stays readable in a narrow window and does not sprawl in a wide one.
            int listWidth = Math.Max(220, Math.Min(400, width / 3));

            _list.Left = Gutter;
            _list.Width = listWidth;
            _list.Height = Math.Max(60, ClientSize.Height - _list.Top - Gutter);

            _detail.Left = _list.Right + Gutter;
            _detail.Width = Math.Max(120, ClientSize.Width - _detail.Left - Gutter);
            _detail.Height = _list.Height;
        }

        private void DrawAlertRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _alerts.Count) return;

            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);

            WeatherAlert alert = _alerts[e.Index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var background = new SolidBrush(selected ? Theme.SurfaceAlt : Theme.Surface))
            {
                g.FillRectangle(background, e.Bounds);
            }

            Color accent = Theme.AlertColor(alert.Severity, alert.IsWarning);

            // A colour bar down the left edge, so severity is readable without
            // depending on the text colour alone.
            using (var brush = new SolidBrush(accent))
            {
                g.FillRectangle(brush, e.Bounds.X, e.Bounds.Y + 4, 4, e.Bounds.Height - 8);
            }

            var textBounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 5, e.Bounds.Width - 18, 18);
            Theme.DrawText(g, alert.Event ?? "Weather Alert", Theme.FontBodyBold, accent,
                textBounds, wrap: false);

            string subtitle = alert.AreaDescription ?? string.Empty;
            if (alert.Expires.HasValue)
            {
                subtitle = "Until " + alert.Expires.Value.ToString("ddd h:mm tt")
                           + (string.IsNullOrEmpty(subtitle) ? string.Empty : "  ·  " + subtitle);
            }

            Theme.DrawText(g, subtitle, Theme.FontSmall, Theme.TextMuted,
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 24, e.Bounds.Width - 18, 18), wrap: false);
        }

        private void ShowDetail()
        {
            int index = _list.SelectedIndex;
            if (index < 0 || index >= _alerts.Count) return;

            WeatherAlert alert = _alerts[index];
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
            _detail.Select(0, 0);
        }

        private static void AppendField(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            builder.Append(label).Append(": ").AppendLine(value);
        }

        /// <summary>
        /// NWS product text arrives hard-wrapped to the old teletype width, which
        /// looks ragged in a resizable window. Single newlines are unwrapped into
        /// spaces while blank lines are kept as paragraph breaks.
        /// </summary>
        private static string Rewrap(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string[] paragraphs = text.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None);
            var result = new StringBuilder();

            for (int i = 0; i < paragraphs.Length; i++)
            {
                string paragraph = paragraphs[i].Replace('\n', ' ').Trim();
                while (paragraph.Contains("  ")) paragraph = paragraph.Replace("  ", " ");

                if (paragraph.Length == 0) continue;

                if (result.Length > 0) result.AppendLine().AppendLine();
                result.Append(paragraph);
            }

            return result.ToString();
        }
    }
}
