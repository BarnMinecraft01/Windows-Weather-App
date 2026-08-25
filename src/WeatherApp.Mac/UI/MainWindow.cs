using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WeatherApp.Configuration;
using WeatherApp.Models;
using WeatherApp.Net;
using WeatherApp.Platform;
using WeatherApp.Services;
using Region = WeatherApp.Models.Region;

namespace WeatherApp.UI
{
    /// <summary>
    /// The application window: a place picker, six tabs, and a status line.
    ///
    /// One refresh populates every tab from a single snapshot, so the numbers on
    /// the forecast tab and the precipitation tab can never disagree. Radar and the
    /// jet stream map load on demand instead, because they are the two expensive
    /// fetches and most sessions never open them.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private const double ToolbarHeight = 88;

        private readonly AppSettings _settings;
        private readonly WeatherAggregator _aggregator = new WeatherAggregator();

        private readonly ComboBox _regionBox;
        private readonly ComboBox _locationBox;
        private readonly ComboBox _alertScopeBox;
        private readonly CheckBox _metricBox;
        private readonly Button _removeButton;
        private readonly Button _refreshButton;
        private readonly TextBlock _statusText;
        private readonly TabControl _tabs;
        private readonly Canvas _toolbar;

        private readonly NowPanel _nowPanel = new NowPanel();
        private readonly ForecastPanel _forecastPanel = new ForecastPanel();
        private readonly PrecipitationPanel _precipitationPanel = new PrecipitationPanel();
        private readonly RadarPanel _radarPanel = new RadarPanel();
        private readonly JetStreamPanel _jetStreamPanel = new JetStreamPanel();
        private readonly AlertsPanel _alertsPanel = new AlertsPanel();
        private readonly TabItem _alertsTab;

        private readonly DispatcherTimer _refreshTimer;
        private readonly HashSet<string> _announcedAlertIds = new HashSet<string>(StringComparer.Ordinal);

        private INotifier _notifier = new NullNotifier();
        private CancellationTokenSource _cancellation;
        private bool _suppressEvents;

        public MainWindow(AppSettings settings)
        {
            _settings = settings;
            Units.UseMetric = settings.UseMetric;

            Title = "Windows Weather";
            Width = 1200;
            Height = 800;
            MinWidth = 940;
            MinHeight = 640;
            Background = Theme.BackgroundBrush;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ---- toolbar ------------------------------------------------------
            _regionBox = Widgets.Combo(220);
            _regionBox.ItemsSource = RegionCatalog.Regions.ToList();
            _regionBox.SelectionChanged += OnRegionChanged;

            _locationBox = Widgets.Combo(260);
            _locationBox.SelectionChanged += OnLocationChanged;

            Button addButton = Widgets.Button("Add...", 82);
            addButton.Click += OnAddLocation;

            _removeButton = Widgets.Button("Remove", 86);
            _removeButton.Click += OnRemoveLocation;

            _refreshButton = Widgets.Button("Refresh", 90);
            _refreshButton.Click += async (s, e) => await RefreshAsync(true).ConfigureAwait(true);

            _alertScopeBox = Widgets.Combo(160);
            _alertScopeBox.ItemsSource = new List<ScopeChoice>
            {
                new ScopeChoice("point", "This location"),
                new ScopeChoice("region", "Whole region")
            };
            _alertScopeBox.SelectedIndex =
                string.Equals(settings.AlertScope, "point", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            _alertScopeBox.SelectionChanged += OnAlertScopeChanged;

            _metricBox = Widgets.Check("Metric units", settings.UseMetric);
            _metricBox.IsCheckedChanged += OnMetricChanged;

            TextBlock regionCaption = Widgets.Caption("Region");
            TextBlock locationCaption = Widgets.Caption("Location");
            TextBlock scopeCaption = Widgets.Caption("Alerts for");

            _toolbar = new Canvas { Height = ToolbarHeight, Background = Theme.BackgroundBrush };
            _toolbar.Children.Add(regionCaption);
            _toolbar.Children.Add(_regionBox);
            _toolbar.Children.Add(locationCaption);
            _toolbar.Children.Add(_locationBox);
            _toolbar.Children.Add(addButton);
            _toolbar.Children.Add(_removeButton);
            _toolbar.Children.Add(_refreshButton);
            _toolbar.Children.Add(scopeCaption);
            _toolbar.Children.Add(_alertScopeBox);
            _toolbar.Children.Add(_metricBox);

            LayoutToolbar(regionCaption, locationCaption, scopeCaption, addButton);

            // ---- tabs ---------------------------------------------------------
            _tabs = new TabControl
            {
                Background = Theme.BackgroundBrush,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };

            _tabs.Items.Add(MakeTab("Now", _nowPanel));
            _tabs.Items.Add(MakeTab("10-Day Forecast", _forecastPanel));
            _tabs.Items.Add(MakeTab("Precipitation", _precipitationPanel));
            _tabs.Items.Add(MakeTab("Radar", _radarPanel));
            _tabs.Items.Add(MakeTab("Jet Stream", _jetStreamPanel));

            _alertsTab = MakeTab("Alerts", _alertsPanel);
            _tabs.Items.Add(_alertsTab);

            _tabs.SelectionChanged += (s, e) =>
            {
                var item = _tabs.SelectedItem as TabItem;
                if (item == null) return;

                var panel = item.Content as WeatherPanel;
                if (panel != null) panel.OnActivated();
            };

            _statusText = Widgets.Label("Ready.", Theme.TextMuted);
            _statusText.Margin = new Thickness(16, 4, 16, 6);

            var layout = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_toolbar, Dock.Top);
            DockPanel.SetDock(_statusText, Dock.Bottom);
            layout.Children.Add(_toolbar);
            layout.Children.Add(_statusText);
            layout.Children.Add(_tabs);

            Content = layout;

            _jetStreamPanel.LevelChanged += (s, level) =>
            {
                _settings.JetStreamLevel = level;
                _settings.Save();
            };

            // Created on the UI thread; Avalonia 12 binds a DispatcherTimer to the
            // dispatcher that is current when it is constructed.
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Math.Max(5, settings.AutoRefreshMinutes))
            };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync(false).ConfigureAwait(true);

            Opened += OnOpened;
            Closing += OnClosing;
        }

        private static TabItem MakeTab(string title, WeatherPanel panel)
        {
            return new TabItem
            {
                Header = title,
                Content = panel,
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeBody,
                Foreground = Theme.Text
            };
        }

        private void LayoutToolbar(TextBlock regionCaption, TextBlock locationCaption,
            TextBlock scopeCaption, Button addButton)
        {
            void At(Control control, double left, double top, double width, double height)
            {
                Canvas.SetLeft(control, left);
                Canvas.SetTop(control, top);
                control.Width = width;
                control.Height = height;
            }

            At(regionCaption, 16, 12, 200, 16);
            At(_regionBox, 16, 32, 220, 28);

            At(locationCaption, 248, 12, 200, 16);
            At(_locationBox, 248, 32, 260, 28);

            At(addButton, 518, 32, 82, 28);
            At(_removeButton, 608, 32, 86, 28);
            At(_refreshButton, 706, 32, 90, 28);

            At(scopeCaption, 812, 12, 200, 16);
            At(_alertScopeBox, 812, 32, 160, 28);

            At(_metricBox, 986, 32, 140, 28);
        }

        // ---- lifecycle -------------------------------------------------------

        private async void OnOpened(object sender, EventArgs e)
        {
            if (_settings.NotifyOnWarnings) _notifier = new ToastNotifier(this);

            _suppressEvents = true;
            SelectRegion(_settings.RegionId);
            RebuildLocationList();
            _suppressEvents = false;

            _refreshTimer.Start();
            await RefreshAsync(false).ConfigureAwait(true);
        }

        private void OnClosing(object sender, EventArgs e)
        {
            CancelPending();
            _refreshTimer.Stop();

            _settings.RegionId = CurrentRegion.Id;
            _settings.UseMetric = _metricBox.IsChecked == true;
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
            var regions = _regionBox.ItemsSource as List<Region>;
            if (regions == null) return;

            for (int i = 0; i < regions.Count; i++)
            {
                if (ReferenceEquals(regions[i], target))
                {
                    _regionBox.SelectedIndex = i;
                    return;
                }
            }

            if (regions.Count > 0) _regionBox.SelectedIndex = 0;
        }

        private void RebuildLocationList()
        {
            var labels = _settings.SavedLocations.Select(l => l.DisplayName).ToList();
            _locationBox.ItemsSource = labels;

            if (labels.Count > 0)
            {
                int index = _settings.ActiveLocationIndex;
                if (index < 0 || index >= labels.Count) index = 0;
                _locationBox.SelectedIndex = index;
            }

            _removeButton.IsEnabled = _settings.SavedLocations.Count > 1;
        }

        // ---- events ----------------------------------------------------------

        private async void OnRegionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            Region region = CurrentRegion;
            _settings.RegionId = region.Id;

            // Offer the region's own places when none of the saved ones fall in it.
            bool anyInRegion = _settings.SavedLocations.Any(
                l => ReferenceEquals(RegionCatalog.ForLocation(l), region));

            if (!anyInRegion && region.DefaultLocations.Count > 0)
            {
                _suppressEvents = true;
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
            await RefreshAsync(false).ConfigureAwait(true);
        }

        private async void OnLocationChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _settings.ActiveLocationIndex = Math.Max(0, _locationBox.SelectedIndex);
            _settings.Save();
            await RefreshAsync(false).ConfigureAwait(true);
        }

        private async void OnAddLocation(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var picker = new LocationPickerWindow();
            bool accepted = await picker.ShowDialog<bool>(this).ConfigureAwait(true);

            if (!accepted || picker.SelectedLocation == null) return;

            _settings.SavedLocations.Add(picker.SelectedLocation);
            _settings.ActiveLocationIndex = _settings.SavedLocations.Count - 1;
            _settings.Save();

            _suppressEvents = true;
            RebuildLocationList();
            _suppressEvents = false;

            await RefreshAsync(false).ConfigureAwait(true);
        }

        private async void OnRemoveLocation(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            int index = _locationBox.SelectedIndex;
            if (index < 0 || _settings.SavedLocations.Count <= 1) return;

            _settings.SavedLocations.RemoveAt(index);
            _settings.ActiveLocationIndex = Math.Min(index, _settings.SavedLocations.Count - 1);
            _settings.Save();

            _suppressEvents = true;
            RebuildLocationList();
            _suppressEvents = false;

            await RefreshAsync(false).ConfigureAwait(true);
        }

        private async void OnAlertScopeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            var choice = _alertScopeBox.SelectedItem as ScopeChoice;
            _settings.AlertScope = choice == null ? "region" : choice.Value;
            _settings.Save();

            await RefreshAsync(true).ConfigureAwait(true);
        }

        private void OnMetricChanged(object sender, EventArgs e)
        {
            bool metric = _metricBox.IsChecked == true;

            Units.UseMetric = metric;
            _settings.UseMetric = metric;
            _settings.Save();

            // Units are a formatting concern only; no refetch is needed.
            _nowPanel.InvalidateSurface();
            _forecastPanel.InvalidateSurface();
            _precipitationPanel.InvalidateSurface();
            _alertsPanel.InvalidateSurface();
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

            _refreshButton.IsEnabled = false;
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

                int alertCount = snapshot.Alerts.Alerts.Count;
                _alertsTab.Header = alertCount == 0 ? "Alerts" : "Alerts (" + alertCount + ")";
                _alertsTab.Foreground = alertCount == 0 ? Theme.Text : Theme.Brush(Theme.DangerColor);

                AnnounceNewWarnings(snapshot);
                _settings.Save();

                string status = "Updated " + snapshot.RetrievedAt.ToString("h:mm:ss tt");
                if (!string.IsNullOrEmpty(snapshot.ForecastOfficeName))
                {
                    status += "  ·  NWS " + snapshot.ForecastOfficeName;
                }
                status += "  ·  next refresh in " + _settings.AutoRefreshMinutes + " min";

                if (snapshot.Warnings.Count > 0)
                {
                    SetStatus(status + "  ·  " + string.Join("  ", snapshot.Warnings),
                        Theme.Brush(Theme.WarningColor));
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
                SetStatus(ex.Message, Theme.Brush(Theme.DangerColor));
            }
            finally
            {
                _refreshButton.IsEnabled = true;
            }
        }

        private void ConfigureRegionPanels()
        {
            Region region = CurrentRegion;
            GeoLocation location = CurrentLocation;

            _radarPanel.Configure(region, location == null ? null : location.RadarStation,
                _settings.LastRadarSector);
            _jetStreamPanel.Configure(region, location, _settings.JetStreamLevel);
        }

        /// <summary>
        /// Announces a warning the first time it appears, tracked by alert id so it
        /// is not repeated at every refresh for as long as it stays active -- which
        /// would train the user to ignore it.
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
            string message = fresh.Count == 1
                ? (lead.AreaDescription ?? snapshot.Alerts.ScopeDescription ?? string.Empty)
                : fresh.Count + " new warnings are in effect. Open the Alerts tab for details.";

            _notifier.Notify(lead.Event ?? "Weather warning", message, true);
        }

        private void SetStatus(string text, IBrush brush)
        {
            _statusText.Text = text;
            _statusText.Foreground = brush;
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
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
