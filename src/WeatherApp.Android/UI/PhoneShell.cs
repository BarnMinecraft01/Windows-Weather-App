using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
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
    /// The phone shell: a title bar, four destinations, and a settings sheet.
    ///
    /// Four rather than the desktop's six. Precipitation folds into the forecast
    /// as expandable rows, and radar and the jet stream share a "Maps"
    /// destination, because a bottom bar stops being usable past four or five
    /// targets. The chrome the desktop head puts in a toolbar -- location, region,
    /// units -- moves into a sheet, since a phone has no room for a permanent
    /// toolbar.
    /// </summary>
    public sealed class PhoneShell : UserControl
    {
        private const double TopBarHeight = 54;
        private const double NavBarHeight = 62;

        private readonly AppSettings _settings;
        private readonly WeatherAggregator _aggregator = new WeatherAggregator();
        private readonly GeocodingClient _geocoder = new GeocodingClient();

        private readonly PhoneNowScreen _now = new PhoneNowScreen();
        private readonly PhoneForecastScreen _forecast = new PhoneForecastScreen();
        private readonly PhoneMapsScreen _maps = new PhoneMapsScreen();
        private readonly PhoneAlertsScreen _alerts = new PhoneAlertsScreen();

        private readonly Button _locationButton;
        private readonly Button _refreshButton;
        private readonly TextBlock _statusText;
        private readonly Panel _contentHost;
        private readonly Border _sheet;
        private readonly List<Button> _navButtons = new List<Button>();

        private readonly ComboBox _regionBox;
        private readonly ListBox _savedList;
        private readonly TextBox _searchBox;
        private readonly ListBox _searchResults;
        private readonly TextBlock _searchHint;
        private readonly CheckBox _metricBox;
        private readonly List<GeoLocation> _matches = new List<GeoLocation>();

        private readonly DispatcherTimer _refreshTimer;
        private readonly HashSet<string> _announcedAlertIds = new HashSet<string>(StringComparer.Ordinal);

        private INotifier _notifier = new NullNotifier();
        private CancellationTokenSource _cancellation;
        private CancellationTokenSource _searchCancellation;
        private bool _suppressEvents;
        private int _active;

        public PhoneShell(AppSettings settings)
        {
            _settings = settings;
            Units.UseMetric = settings.UseMetric;
            Background = AppTheme.BackgroundBrush;

            // ---- top bar ------------------------------------------------------
            _locationButton = Widgets.Button("Location", 200);
            _locationButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            _locationButton.Click += (s, e) => ToggleSheet(true);

            _refreshButton = Widgets.Button("Refresh", 96);
            _refreshButton.Click += async (s, e) => await RefreshAsync(true).ConfigureAwait(true);

            _statusText = Widgets.Label(string.Empty, AppTheme.TextFaint);

            var topBar = new DockPanel { Height = TopBarHeight, Margin = new Thickness(10, 8, 10, 0) };
            DockPanel.SetDock(_refreshButton, Dock.Right);
            topBar.Children.Add(_refreshButton);
            topBar.Children.Add(_locationButton);

            // ---- content ------------------------------------------------------
            _contentHost = new Panel();
            _contentHost.Children.Add(_now);
            _contentHost.Children.Add(_forecast);
            _contentHost.Children.Add(_maps);
            _contentHost.Children.Add(_alerts);

            // ---- bottom navigation --------------------------------------------
            var navBar = new UniformGrid
            {
                Rows = 1,
                Columns = 4,
                Height = NavBarHeight,
                Background = AppTheme.SurfaceBrush
            };

            string[] labels = { "Now", "Forecast", "Maps", "Alerts" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button button = Widgets.Button(labels[i], 80);
                // Let the grid cell decide the width so the four targets divide the
                // bar evenly whatever the screen size.
                button.Width = double.NaN;
                button.Margin = new Thickness(4, 8);
                button.Click += (s, e) => Show(index);

                _navButtons.Add(button);
                navBar.Children.Add(button);
            }

            var main = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(topBar, Dock.Top);
            DockPanel.SetDock(_statusText, Dock.Top);
            DockPanel.SetDock(navBar, Dock.Bottom);

            _statusText.Margin = new Thickness(14, 2, 14, 4);

            main.Children.Add(topBar);
            main.Children.Add(_statusText);
            main.Children.Add(navBar);
            main.Children.Add(_contentHost);

            // ---- settings sheet -----------------------------------------------
            _regionBox = Widgets.Combo(240);
            _regionBox.ItemsSource = RegionCatalog.Regions.ToList();
            _regionBox.SelectionChanged += OnRegionChanged;

            _savedList = MakeList(170);
            _savedList.SelectionChanged += OnSavedSelected;

            _searchBox = Widgets.Input(200);
            _searchBox.PlaceholderText = "Town, ZIP, or lat, lon";

            Button searchButton = Widgets.Button("Search", 96);
            searchButton.Click += (s, e) => BeginSearch();

            _searchResults = MakeList(140);
            _searchResults.SelectionChanged += OnSearchResultChosen;

            _searchHint = Widgets.Label("Add a place to follow.", AppTheme.TextFaint);

            _metricBox = Widgets.Check("Metric units", settings.UseMetric);
            _metricBox.IsCheckedChanged += OnMetricChanged;

            Button closeButton = Widgets.Button("Done", 110);
            closeButton.Click += (s, e) => ToggleSheet(false);

            var sheetContent = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
            sheetContent.Children.Add(Widgets.Caption("Region"));
            sheetContent.Children.Add(_regionBox);
            sheetContent.Children.Add(Widgets.Caption("Saved places"));
            sheetContent.Children.Add(_savedList);
            sheetContent.Children.Add(Widgets.Caption("Add a place"));
            sheetContent.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _searchBox, searchButton }
            });
            sheetContent.Children.Add(_searchResults);
            sheetContent.Children.Add(_searchHint);
            sheetContent.Children.Add(_metricBox);
            sheetContent.Children.Add(closeButton);

            _sheet = new Border
            {
                Background = AppTheme.BackgroundBrush,
                IsVisible = false,
                Child = new ScrollViewer { Content = sheetContent }
            };

            var root = new Grid();
            root.Children.Add(main);
            root.Children.Add(_sheet);
            Content = root;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Math.Max(5, settings.AutoRefreshMinutes))
            };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync(false).ConfigureAwait(true);

            Show(0);
        }

        private static ListBox MakeList(double height)
        {
            return new ListBox
            {
                Height = height,
                Background = AppTheme.SurfaceBrush,
                Foreground = AppTheme.Text,
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody,
                BorderBrush = AppTheme.Brush(AppTheme.BorderColor),
                BorderThickness = new Thickness(1)
            };
        }

        protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (_settings.NotifyOnWarnings) TryCreateNotifier();

            _suppressEvents = true;
            SelectRegion(_settings.RegionId);
            RebuildSavedList();
            _suppressEvents = false;

            _refreshTimer.Start();
            await RefreshAsync(false).ConfigureAwait(true);
        }

        /// <summary>
        /// Avalonia's in-window toast is used rather than a system notification.
        /// A real Notification Center entry needs a notification channel and a
        /// foreground service to be useful when the app is closed, which is a
        /// separate piece of work; a toast at least cannot fail silently.
        /// </summary>
        private void TryCreateNotifier()
        {
            try
            {
                TopLevel topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null) _notifier = new ToastNotifier(topLevel);
            }
            catch (Exception)
            {
                _notifier = new NullNotifier();
            }
        }

        // ---- navigation ------------------------------------------------------

        private void Show(int index)
        {
            _active = index;

            var screens = new WeatherPanel[] { _now, _forecast, _maps, _alerts };
            for (int i = 0; i < screens.Length; i++)
            {
                screens[i].IsVisible = i == index;
                _navButtons[i].Background = i == index ? AppTheme.Accent : AppTheme.SurfaceAltBrush;
            }

            screens[index].OnActivated();
        }

        private void ToggleSheet(bool open)
        {
            _sheet.IsVisible = open;
        }

        // ---- settings sheet events -------------------------------------------

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
        }

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
                int index = _settings.ActiveLocationIndex;
                if (index < 0 || index >= _settings.SavedLocations.Count)
                {
                    return _settings.SavedLocations.FirstOrDefault();
                }
                return _settings.SavedLocations[index];
            }
        }

        private void RebuildSavedList()
        {
            _savedList.ItemsSource = _settings.SavedLocations.Select(l => l.DisplayName).ToList();

            int index = _settings.ActiveLocationIndex;
            if (index < 0 || index >= _settings.SavedLocations.Count) index = 0;
            if (_settings.SavedLocations.Count > 0) _savedList.SelectedIndex = index;

            GeoLocation current = CurrentLocation;
            _locationButton.Content = current == null ? "Add a location" : current.DisplayName;
        }

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
                    bool saved = _settings.SavedLocations.Any(l =>
                        Math.Abs(l.Latitude - location.Latitude) < 0.001 &&
                        Math.Abs(l.Longitude - location.Longitude) < 0.001);

                    if (saved) continue;

                    if (firstAdded < 0) firstAdded = _settings.SavedLocations.Count;
                    _settings.SavedLocations.Add(location);
                }

                if (firstAdded >= 0) _settings.ActiveLocationIndex = firstAdded;
                RebuildSavedList();
                _suppressEvents = false;
            }

            _settings.Save();
            await RefreshAsync(false).ConfigureAwait(true);
        }

        private async void OnSavedSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            int index = _savedList.SelectedIndex;
            if (index < 0 || index >= _settings.SavedLocations.Count) return;
            if (index == _settings.ActiveLocationIndex) return;

            _settings.ActiveLocationIndex = index;
            _settings.Save();

            GeoLocation current = CurrentLocation;
            _locationButton.Content = current == null ? "Add a location" : current.DisplayName;

            ToggleSheet(false);
            await RefreshAsync(false).ConfigureAwait(true);
        }

        private void OnMetricChanged(object sender, EventArgs e)
        {
            bool metric = _metricBox.IsChecked == true;

            Units.UseMetric = metric;
            _settings.UseMetric = metric;
            _settings.Save();

            // Units are formatting only; no refetch, just a repaint.
            _now.InvalidateSurface();
            _forecast.InvalidateSurface();
            _alerts.InvalidateSurface();
        }

        private async void BeginSearch()
        {
            string query = _searchBox.Text;
            if (string.IsNullOrWhiteSpace(query)) return;

            if (_searchCancellation != null)
            {
                _searchCancellation.Cancel();
                _searchCancellation.Dispose();
            }
            _searchCancellation = new CancellationTokenSource();

            _searchHint.Text = "Searching...";
            _matches.Clear();
            _searchResults.ItemsSource = null;

            try
            {
                List<GeoLocation> found = await _geocoder
                    .SearchAsync(query, _searchCancellation.Token)
                    .ConfigureAwait(true);

                _matches.AddRange(found);
                _searchResults.ItemsSource = found
                    .Select(l => (string.IsNullOrWhiteSpace(l.Name) ? l.Coordinates : l.DisplayName))
                    .ToList();

                _searchHint.Text = found.Count == 0
                    ? "Nothing matched. Try a nearby larger town."
                    : "Tap a result to add it.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                _searchHint.Text = ex.Message;
            }
        }

        private async void OnSearchResultChosen(object sender, SelectionChangedEventArgs e)
        {
            int index = _searchResults.SelectedIndex;
            if (index < 0 || index >= _matches.Count) return;

            _settings.SavedLocations.Add(_matches[index]);
            _settings.ActiveLocationIndex = _settings.SavedLocations.Count - 1;
            _settings.Save();

            _suppressEvents = true;
            RebuildSavedList();
            _searchResults.ItemsSource = null;
            _matches.Clear();
            _suppressEvents = false;

            ToggleSheet(false);
            await RefreshAsync(false).ConfigureAwait(true);
        }

        // ---- refresh ---------------------------------------------------------

        private async Task RefreshAsync(bool force)
        {
            GeoLocation location = CurrentLocation;
            if (location == null)
            {
                _statusText.Text = "Add a location to get started.";
                ToggleSheet(true);
                return;
            }

            if (_cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            if (force) HttpService.ClearCache();

            _refreshButton.IsEnabled = false;
            _statusText.Text = "Refreshing...";
            _statusText.Foreground = AppTheme.TextFaint;

            try
            {
                WeatherSnapshot snapshot = await _aggregator
                    .RefreshAsync(location, CurrentRegion, _settings.AlertScope, token)
                    .ConfigureAwait(true);

                if (token.IsCancellationRequested) return;

                _now.ApplySnapshot(snapshot);
                _forecast.ApplySnapshot(snapshot);
                _maps.ApplySnapshot(snapshot);
                _alerts.ApplySnapshot(snapshot);

                _maps.Configure(CurrentRegion, location, snapshot.RadarStation, _settings.JetStreamLevel);

                int alertCount = snapshot.Alerts.Alerts.Count;
                _navButtons[3].Content = alertCount == 0 ? "Alerts" : "Alerts (" + alertCount + ")";
                _navButtons[3].Foreground = alertCount == 0
                    ? AppTheme.Text
                    : AppTheme.Brush(AppTheme.DangerColor);

                AnnounceNewWarnings(snapshot);
                _settings.Save();

                _statusText.Text = "Updated " + snapshot.RetrievedAt.ToString("h:mm tt")
                    + (snapshot.Warnings.Count > 0 ? "  ·  " + snapshot.Warnings[0] : string.Empty);
                _statusText.Foreground = snapshot.Warnings.Count > 0
                    ? AppTheme.Brush(AppTheme.WarningColor)
                    : AppTheme.TextFaint;
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                _statusText.Text = ex.Message;
                _statusText.Foreground = AppTheme.Brush(AppTheme.DangerColor);
            }
            finally
            {
                _refreshButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Announces a warning the first time it appears, tracked by alert id so it
        /// is not repeated at every refresh for as long as it stays active.
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
                ? (lead.AreaDescription ?? string.Empty)
                : fresh.Count + " new warnings are in effect.";

            _notifier.Notify(lead.Event ?? "Weather warning", message, true);
        }
    }
}
