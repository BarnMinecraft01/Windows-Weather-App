using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
// System.Threading.Timer is also in scope here; the UI one is what is wanted.
using Timer = System.Windows.Forms.Timer;
using WeatherApp.Configuration;
using WeatherApp.Models;
// System.Drawing also defines a Region; this file means the geographic one.
using Region = WeatherApp.Models.Region;
using WeatherApp.Net;
using WeatherApp.Services;

namespace WeatherApp.UI
{
    /// <summary>
    /// The application window: a place picker, six tabs, and a status line.
    ///
    /// One refresh populates every tab from a single snapshot, so the numbers on
    /// the forecast tab and the numbers on the precipitation tab can never
    /// disagree with each other. Radar and the jet stream map load on demand
    /// instead, because they are the two expensive fetches and most sessions never
    /// open them.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly WeatherAggregator _aggregator = new WeatherAggregator();

        private readonly ComboBox _regionBox;
        private readonly ComboBox _locationBox;
        private readonly ComboBox _alertScopeBox;
        private readonly CheckBox _metricBox;
        private readonly Button _addButton;
        private readonly Button _removeButton;
        private readonly Button _refreshButton;
        private readonly Label _statusLabel;
        private readonly TabControl _tabs;

        private readonly NowPanel _nowPanel = new NowPanel();
        private readonly ForecastPanel _forecastPanel = new ForecastPanel();
        private readonly PrecipitationPanel _precipitationPanel = new PrecipitationPanel();
        private readonly RadarPanel _radarPanel = new RadarPanel();
        private readonly JetStreamPanel _jetStreamPanel = new JetStreamPanel();
        private readonly AlertsPanel _alertsPanel = new AlertsPanel();

        private readonly Timer _refreshTimer = new Timer();
        private readonly NotifyIcon _notifyIcon;

        private CancellationTokenSource _cancellation;
        private bool _suppressEvents;
        private readonly HashSet<string> _announcedAlertIds = new HashSet<string>(StringComparer.Ordinal);

        private const int ToolbarHeight = 84;
        private const int StatusHeight = 26;

        public MainForm(AppSettings settings)
        {
            _settings = settings;
            Units.UseMetric = settings.UseMetric;

            Text = "Windows Weather";
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;

            // ---- toolbar ------------------------------------------------------
            var regionLabel = MakeLabel("Region", 14, 12);
            _regionBox = MakeCombo(14, 30, 210);
            foreach (Region region in RegionCatalog.Regions) _regionBox.Items.Add(region);
            _regionBox.SelectedIndexChanged += OnRegionChanged;

            var locationLabel = MakeLabel("Location", _regionBox.Right + 12, 12);
            _locationBox = MakeCombo(_regionBox.Right + 12, 30, 250);
            _locationBox.SelectedIndexChanged += OnLocationChanged;

            _addButton = Theme.CreateButton("Add...", 74);
            _addButton.Left = _locationBox.Right + 8;
            _addButton.Top = 29;
            _addButton.Click += OnAddLocation;

            _removeButton = Theme.CreateButton("Remove", 78);
            _removeButton.Left = _addButton.Right + 6;
            _removeButton.Top = 29;
            _removeButton.Click += OnRemoveLocation;

            _refreshButton = Theme.CreateButton("Refresh", 84);
            _refreshButton.Left = _removeButton.Right + 14;
            _refreshButton.Top = 29;
            _refreshButton.Click += async (s, e) => await RefreshAsync(force: true).ConfigureAwait(true);

            var scopeLabel = MakeLabel("Alerts for", _refreshButton.Right + 16, 12);
            _alertScopeBox = MakeCombo(_refreshButton.Right + 16, 30, 150);
            _alertScopeBox.Items.Add(new ScopeChoice("point", "This location"));
            _alertScopeBox.Items.Add(new ScopeChoice("region", "Whole region"));
            _alertScopeBox.SelectedIndex =
                string.Equals(settings.AlertScope, "point", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            _alertScopeBox.SelectedIndexChanged += OnAlertScopeChanged;

            _metricBox = new CheckBox
            {
                Text = "Metric units",
                Left = _alertScopeBox.Right + 16,
                Top = 32,
                Width = 110,
                ForeColor = Theme.Text,
                BackColor = Theme.Background,
                FlatStyle = FlatStyle.Flat,
                Checked = settings.UseMetric
            };
            _metricBox.CheckedChanged += OnMetricChanged;

            // ---- tabs ---------------------------------------------------------
            _tabs = new TabControl
            {
                Left = 0,
                Top = ToolbarHeight,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(132, 30),
                Appearance = TabAppearance.Normal
            };
            _tabs.DrawItem += DrawTabHeader;
            _tabs.SelectedIndexChanged += OnTabChanged;

            AddTab("Now", _nowPanel);
            AddTab("10-Day Forecast", _forecastPanel);
            AddTab("Precipitation", _precipitationPanel);
            AddTab("Radar", _radarPanel);
            AddTab("Jet Stream", _jetStreamPanel);
            AddTab("Alerts", _alertsPanel);

            _statusLabel = new Label
            {
                Left = 14,
                Height = StatusHeight - 6,
                AutoSize = false,
                ForeColor = Theme.TextMuted,
                Font = Theme.FontSmall,
                Text = "Ready."
            };

            Controls.Add(regionLabel);
            Controls.Add(_regionBox);
            Controls.Add(locationLabel);
            Controls.Add(_locationBox);
            Controls.Add(_addButton);
            Controls.Add(_removeButton);
            Controls.Add(_refreshButton);
            Controls.Add(scopeLabel);
            Controls.Add(_alertScopeBox);
            Controls.Add(_metricBox);
            Controls.Add(_tabs);
            Controls.Add(_statusLabel);

            _jetStreamPanel.LevelChanged += (s, level) =>
            {
                _settings.JetStreamLevel = level;
                _settings.Save();
            };

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = false,
                Text = "Windows Weather"
            };

            _refreshTimer.Interval = Math.Max(5, settings.AutoRefreshMinutes) * 60 * 1000;
            _refreshTimer.Tick += async (s, e) => await RefreshAsync(force: false).ConfigureAwait(true);

            Resize += (s, e) => LayoutChildren();
            Load += OnLoaded;
            FormClosing += OnClosing;
        }

        // ---- setup -----------------------------------------------------------

        private static Label MakeLabel(string text, int left, int top)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(),
                Left = left,
                Top = top,
                Width = 160,
                Height = 15,
                ForeColor = Theme.TextFaint,
                Font = Theme.FontSmall,
                BackColor = Theme.Background
            };
        }

        private static ComboBox MakeCombo(int left, int top, int width)
        {
            return new ComboBox
            {
                Left = left,
                Top = top,
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                Font = Theme.FontBody
            };
        }

        private void AddTab(string title, WeatherPanel panel)
        {
            var page = new TabPage(title)
            {
                BackColor = Theme.Background,
                ForeColor = Theme.Text,
                Padding = new System.Windows.Forms.Padding(0)
            };

            panel.Dock = DockStyle.Fill;
            page.Controls.Add(panel);
            _tabs.TabPages.Add(page);
        }

        private void LayoutChildren()
        {
            _tabs.Width = ClientSize.Width;
            _tabs.Height = Math.Max(120, ClientSize.Height - ToolbarHeight - StatusHeight);

            _statusLabel.Top = ClientSize.Height - StatusHeight + 3;
            _statusLabel.Width = Math.Max(200, ClientSize.Width - 28);
        }

        private async void OnLoaded(object sender, EventArgs e)
        {
            LayoutChildren();

            _suppressEvents = true;
            SelectRegion(_settings.RegionId);
            RebuildLocationList();
            _suppressEvents = false;

            _refreshTimer.Start();
            await RefreshAsync(force: false).ConfigureAwait(true);
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            CancelPending();
            _refreshTimer.Stop();
            _notifyIcon.Visible = false;

            _settings.RegionId = CurrentRegion.Id;
            _settings.UseMetric = _metricBox.Checked;
            _settings.LastRadarSector = _radarPanel.SelectedSector;
            _settings.JetStreamLevel = _jetStreamPanel.SelectedLevel;
            _settings.ActiveLocationIndex = Math.Max(0, _locationBox.SelectedIndex);
            _settings.Save();
        }

        // ---- state -----------------------------------------------------------

        private Region CurrentRegion
        {
            get
            {
                var region = _regionBox.SelectedItem as Region;
                return region ?? RegionCatalog.Regions[0];
            }
        }

        private GeoLocation CurrentLocation
        {
            get
            {
                int index = _locationBox.SelectedIndex;
                if (index < 0 || index >= _settings.SavedLocations.Count) return null;
                return _settings.SavedLocations[index];
            }
        }

        private void SelectRegion(string id)
        {
            Region target = RegionCatalog.Find(id);

            for (int i = 0; i < _regionBox.Items.Count; i++)
            {
                if (ReferenceEquals(_regionBox.Items[i], target))
                {
                    _regionBox.SelectedIndex = i;
                    return;
                }
            }

            if (_regionBox.Items.Count > 0) _regionBox.SelectedIndex = 0;
        }

        private void RebuildLocationList()
        {
            _locationBox.BeginUpdate();
            _locationBox.Items.Clear();

            foreach (GeoLocation location in _settings.SavedLocations)
            {
                _locationBox.Items.Add(location.DisplayName);
            }

            if (_locationBox.Items.Count > 0)
            {
                int index = _settings.ActiveLocationIndex;
                if (index < 0 || index >= _locationBox.Items.Count) index = 0;
                _locationBox.SelectedIndex = index;
            }

            _locationBox.EndUpdate();
            _removeButton.Enabled = _settings.SavedLocations.Count > 1;
        }

        // ---- events ----------------------------------------------------------

        private async void OnRegionChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;

            Region region = CurrentRegion;
            _settings.RegionId = region.Id;

            // Offer the region's own places when none of the saved ones are in it.
            bool anyInRegion = _settings.SavedLocations.Any(
                l => ReferenceEquals(RegionCatalog.ForLocation(l), region));

            if (!anyInRegion && region.DefaultLocations.Count > 0)
            {
                _suppressEvents = true;

                // Remember where the first addition lands so the view switches to a
                // place that is actually in the region the user just picked.
                int firstAdded = -1;

                foreach (GeoLocation location in region.DefaultLocations.Take(4))
                {
                    bool alreadySaved = _settings.SavedLocations.Any(l =>
                        Math.Abs(l.Latitude - location.Latitude) < 0.001 &&
                        Math.Abs(l.Longitude - location.Longitude) < 0.001);

                    if (alreadySaved) continue;

                    if (firstAdded < 0) firstAdded = _settings.SavedLocations.Count;
                    _settings.SavedLocations.Add(location);
                }

                if (firstAdded >= 0) _settings.ActiveLocationIndex = firstAdded;

                RebuildLocationList();
                _suppressEvents = false;
            }

            ConfigureRegionPanels();
            _settings.Save();
            await RefreshAsync(force: false).ConfigureAwait(true);
        }

        private async void OnLocationChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;

            _settings.ActiveLocationIndex = Math.Max(0, _locationBox.SelectedIndex);
            _settings.Save();
            await RefreshAsync(force: false).ConfigureAwait(true);
        }

        private async void OnAddLocation(object sender, EventArgs e)
        {
            using (var picker = new LocationPickerForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedLocation == null) return;

                _settings.SavedLocations.Add(picker.SelectedLocation);
                _settings.ActiveLocationIndex = _settings.SavedLocations.Count - 1;
                _settings.Save();

                _suppressEvents = true;
                RebuildLocationList();
                _suppressEvents = false;

                await RefreshAsync(force: false).ConfigureAwait(true);
            }
        }

        private async void OnRemoveLocation(object sender, EventArgs e)
        {
            int index = _locationBox.SelectedIndex;
            if (index < 0 || _settings.SavedLocations.Count <= 1) return;

            _settings.SavedLocations.RemoveAt(index);
            _settings.ActiveLocationIndex = Math.Min(index, _settings.SavedLocations.Count - 1);
            _settings.Save();

            _suppressEvents = true;
            RebuildLocationList();
            _suppressEvents = false;

            await RefreshAsync(force: false).ConfigureAwait(true);
        }

        private async void OnAlertScopeChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;

            var choice = _alertScopeBox.SelectedItem as ScopeChoice;
            _settings.AlertScope = choice == null ? "region" : choice.Value;
            _settings.Save();

            await RefreshAsync(force: true).ConfigureAwait(true);
        }

        private void OnMetricChanged(object sender, EventArgs e)
        {
            Units.UseMetric = _metricBox.Checked;
            _settings.UseMetric = _metricBox.Checked;
            _settings.Save();

            // Units are a formatting concern only; no refetch is needed.
            foreach (TabPage page in _tabs.TabPages)
            {
                foreach (Control control in page.Controls) control.Invalidate(true);
            }
        }

        private void OnTabChanged(object sender, EventArgs e)
        {
            var panel = ActivePanel();
            if (panel != null) panel.OnActivated();
        }

        private WeatherPanel ActivePanel()
        {
            if (_tabs.SelectedTab == null || _tabs.SelectedTab.Controls.Count == 0) return null;
            return _tabs.SelectedTab.Controls[0] as WeatherPanel;
        }

        // ---- refresh ---------------------------------------------------------

        private async Task RefreshAsync(bool force)
        {
            GeoLocation location = CurrentLocation;
            if (location == null)
            {
                SetStatus("Add a location to get started.", Theme.TextMuted);
                return;
            }

            CancelPending();
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            if (force) HttpService.ClearCache();

            _refreshButton.Enabled = false;
            SetStatus("Refreshing " + location.DisplayName + "...", Theme.TextMuted);

            try
            {
                WeatherSnapshot snapshot = await _aggregator
                    .RefreshAsync(location, CurrentRegion, _settings.AlertScope, token)
                    .ConfigureAwait(true);

                if (token.IsCancellationRequested) return;

                _nowPanel.ApplySnapshot(snapshot);
                _forecastPanel.ApplySnapshot(snapshot);
                _precipitationPanel.ApplySnapshot(snapshot);
                _radarPanel.ApplySnapshot(snapshot);
                _jetStreamPanel.ApplySnapshot(snapshot);
                _alertsPanel.ApplySnapshot(snapshot);

                ConfigureRegionPanels();
                UpdateAlertTabTitle(snapshot);
                AnnounceNewWarnings(snapshot);

                // The point lookup fills in the office and station; keep them.
                _settings.Save();

                string status = "Updated " + snapshot.RetrievedAt.ToString("h:mm:ss tt");
                if (!string.IsNullOrEmpty(snapshot.ForecastOfficeName))
                {
                    status += "  ·  NWS " + snapshot.ForecastOfficeName;
                }
                status += "  ·  next refresh in " + _settings.AutoRefreshMinutes + " min";

                if (snapshot.Warnings.Count > 0)
                {
                    status += "  ·  " + string.Join("  ", snapshot.Warnings.ToArray());
                    SetStatus(status, Theme.Warning);
                }
                else
                {
                    SetStatus(status, Theme.TextMuted);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                SetStatus(ex.Message, Theme.Danger);
            }
            finally
            {
                _refreshButton.Enabled = true;
            }
        }

        private void ConfigureRegionPanels()
        {
            Region region = CurrentRegion;
            GeoLocation location = CurrentLocation;

            _radarPanel.Configure(
                region,
                location == null ? null : location.RadarStation,
                _settings.LastRadarSector);

            _jetStreamPanel.Configure(region, location, _settings.JetStreamLevel);
        }

        private void UpdateAlertTabTitle(WeatherSnapshot snapshot)
        {
            TabPage page = _tabs.TabPages[_tabs.TabPages.Count - 1];
            int count = snapshot.Alerts.Alerts.Count;

            page.Text = count == 0 ? "Alerts" : "Alerts (" + count + ")";
            _tabs.Invalidate();
        }

        /// <summary>
        /// Pops a tray notification the first time a warning appears.
        ///
        /// Tracked by alert id so a warning is announced once rather than at every
        /// refresh for as long as it stays active, which would train the user to
        /// ignore it.
        /// </summary>
        private void AnnounceNewWarnings(WeatherSnapshot snapshot)
        {
            if (!_settings.NotifyOnWarnings) return;

            var fresh = snapshot.Alerts.Alerts
                .Where(a => a.IsWarning && !string.IsNullOrEmpty(a.Id) && !_announcedAlertIds.Contains(a.Id))
                .ToList();

            foreach (WeatherAlert alert in snapshot.Alerts.Alerts)
            {
                if (!string.IsNullOrEmpty(alert.Id)) _announcedAlertIds.Add(alert.Id);
            }

            if (fresh.Count == 0) return;

            WeatherAlert lead = fresh.OrderBy(a => a.Priority).First();

            _notifyIcon.Visible = true;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _notifyIcon.BalloonTipTitle = lead.Event ?? "Weather warning";
            _notifyIcon.BalloonTipText = fresh.Count == 1
                ? (lead.AreaDescription ?? snapshot.Alerts.ScopeDescription ?? string.Empty)
                : fresh.Count + " new warnings are in effect. Open the Alerts tab for details.";

            _notifyIcon.ShowBalloonTip(10000);
        }

        private void SetStatus(string text, Color color)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        // ---- tab header drawing ----------------------------------------------

        /// <summary>
        /// Draws the tab strip by hand. The themed control paints a light grey
        /// background that cannot be changed through properties, and it would sit
        /// badly against the dark panels below it.
        /// </summary>
        private void DrawTabHeader(object sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);

            TabPage page = _tabs.TabPages[e.Index];
            bool selected = _tabs.SelectedIndex == e.Index;

            Rectangle bounds = e.Bounds;

            using (var brush = new SolidBrush(selected ? Theme.Surface : Theme.Background))
            {
                g.FillRectangle(brush, bounds);
            }

            if (selected)
            {
                using (var brush = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(brush, bounds.X, bounds.Bottom - 3, bounds.Width, 3);
                }
            }

            Color textColor = selected ? Theme.Text : Theme.TextMuted;

            // An alert count in the tab title should read as urgent, not decorative.
            if (page.Text.StartsWith("Alerts (", StringComparison.Ordinal)) textColor = Theme.Danger;

            Theme.DrawText(g, page.Text, selected ? Theme.FontBodyBold : Theme.FontBody, textColor,
                bounds, StringAlignment.Center, StringAlignment.Center, false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPending();
                _refreshTimer.Dispose();
                _notifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class ScopeChoice
        {
            public ScopeChoice(string value, string title)
            {
                Value = value;
                Title = title;
            }

            public string Value { get; private set; }
            public string Title { get; private set; }

            public override string ToString() { return Title; }
        }
    }
}
