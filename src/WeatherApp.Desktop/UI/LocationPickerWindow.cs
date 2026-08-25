using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WeatherApp.Models;
using WeatherApp.Net;
using WeatherApp.Services;

namespace WeatherApp.UI
{
    /// <summary>
    /// Search dialog for adding a place. Accepts a town name, a US ZIP code, or a
    /// raw "latitude, longitude" pair in the same box.
    /// </summary>
    public sealed class LocationPickerWindow : Window
    {
        private readonly GeocodingClient _geocoder = new GeocodingClient();
        private readonly TextBox _searchBox;
        private readonly ListBox _results;
        private readonly TextBlock _hint;
        private readonly Button _addButton;

        private readonly List<GeoLocation> _matches = new List<GeoLocation>();
        private CancellationTokenSource _cancellation;

        public GeoLocation SelectedLocation { get; private set; }

        public LocationPickerWindow()
        {
            Title = "Add a location";
            Width = 470;
            Height = 380;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.BackgroundBrush;

            _searchBox = Widgets.Input(320);
            // Watermark was renamed PlaceholderText in Avalonia 12.
            _searchBox.PlaceholderText = "Town, ZIP code, or latitude and longitude";
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;

                e.Handled = true;
                BeginSearch();
            };

            Button searchButton = Widgets.Button("Search", 100);
            searchButton.Click += (s, e) => BeginSearch();

            _results = new ListBox
            {
                Background = AppTheme.SurfaceBrush,
                Foreground = AppTheme.Text,
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody,
                BorderBrush = AppTheme.Brush(AppTheme.BorderColor),
                BorderThickness = new Thickness(1)
            };
            _results.SelectionChanged += (s, e) => _addButton.IsEnabled = _results.SelectedIndex >= 0;
            _results.DoubleTapped += (s, e) => Accept();

            _hint = Widgets.Label(
                "Places inside National Weather Service coverage are listed first.", AppTheme.TextFaint);

            _addButton = Widgets.Button("Add", 100);
            _addButton.IsEnabled = false;
            _addButton.Click += (s, e) => Accept();

            Button cancelButton = Widgets.Button("Cancel", 100);
            cancelButton.Click += (s, e) => Close(false);

            var searchRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { _searchBox, searchButton }
            };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { _addButton, cancelButton }
            };

            var layout = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

            DockPanel.SetDock(searchRow, Dock.Top);
            DockPanel.SetDock(buttonRow, Dock.Bottom);
            DockPanel.SetDock(_hint, Dock.Bottom);

            searchRow.Margin = new Thickness(0, 0, 0, 12);
            _hint.Margin = new Thickness(0, 10, 0, 10);

            layout.Children.Add(searchRow);
            layout.Children.Add(buttonRow);
            layout.Children.Add(_hint);
            layout.Children.Add(_results);

            Content = layout;
        }

        private async void BeginSearch()
        {
            string query = _searchBox.Text;
            if (string.IsNullOrWhiteSpace(query)) return;

            if (_cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            _cancellation = new CancellationTokenSource();

            _hint.Foreground = AppTheme.TextFaint;
            _hint.Text = "Searching...";
            _matches.Clear();
            _results.ItemsSource = null;

            try
            {
                List<GeoLocation> found = await _geocoder
                    .SearchAsync(query, _cancellation.Token)
                    .ConfigureAwait(true);

                var labels = new List<string>();
                foreach (GeoLocation location in found)
                {
                    _matches.Add(location);

                    string label = string.IsNullOrWhiteSpace(location.Name)
                        ? location.Coordinates
                        : location.DisplayName;

                    if (!string.IsNullOrWhiteSpace(location.Country) && !location.IsNwsCovered)
                    {
                        label += " (" + location.Country + ")";
                    }

                    labels.Add(label + "   —   " + location.Coordinates);
                }

                _results.ItemsSource = labels;

                if (_matches.Count == 0)
                {
                    _hint.Text = "Nothing matched \"" + query.Trim() + "\". Try a nearby larger town.";
                }
                else
                {
                    _results.SelectedIndex = 0;
                    _hint.Text = _matches.Count == 1
                        ? "1 match."
                        : _matches.Count + " matches. Places inside NWS coverage are listed first.";
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                _hint.Foreground = AppTheme.Brush(AppTheme.WarningColor);
                _hint.Text = ex.Message;
            }
        }

        private void Accept()
        {
            int index = _results.SelectedIndex;
            if (index < 0 || index >= _matches.Count) return;

            SelectedLocation = _matches[index];
            Close(true);
        }
    }
}
