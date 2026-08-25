using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WeatherApp.Configuration;
using WeatherApp.Models;
using WeatherApp.Net;
using WeatherApp.Services;
using WeatherApp.UI.Rendering;
using Region = WeatherApp.Models.Region;

namespace WeatherApp.UI
{
    /// <summary>
    /// The jet stream, drawn from gridded 250 hPa (or 300 hPa) winds.
    ///
    /// Rendered rather than fetched as a picture, for the same reasons as the
    /// WinForms head: the free upper-air charts are static images on NOAA paths
    /// that move, and none of them let you choose a region or a forecast hour.
    ///
    /// The projection is equirectangular with the horizontal axis scaled by the
    /// cosine of the box's centre latitude -- not conformal, and not trying to be;
    /// it keeps shapes roughly right over a region-sized box while staying simple
    /// enough that the grid maths is obviously correct.
    /// </summary>
    public sealed class JetStreamPanel : WeatherPanel
    {
        private const double Gutter = 16;
        private const double ToolbarHeight = 44;
        private const double StatusHeight = 22;

        private readonly OpenMeteoClient _client = new OpenMeteoClient();
        private readonly ComboBox _levelBox;
        private readonly ComboBox _timeBox;
        private readonly Button _refreshButton;
        private readonly Button _chartsButton;

        private JetStreamField _field;
        private CancellationTokenSource _cancellation;
        private Region _region;
        private GeoLocation _marker;
        private string _status = "Open this tab to load upper-air winds.";
        private bool _statusIsError;
        private bool _loaded;

        /// <summary>Raised when the user changes the pressure level, so it can be saved.</summary>
        public event EventHandler<int> LevelChanged;

        public JetStreamPanel()
        {
            _levelBox = Widgets.Combo(168);
            _levelBox.ItemsSource = new List<Choice>
            {
                new Choice(250, "250 hPa (~34,000 ft)"),
                new Choice(300, "300 hPa (~30,000 ft)")
            };
            _levelBox.SelectedIndex = 0;
            _levelBox.SelectionChanged += (s, e) =>
            {
                EventHandler<int> handler = LevelChanged;
                if (handler != null) handler(this, SelectedLevel);
                if (IsEffectivelyVisible) BeginLoad(false);
            };

            _timeBox = Widgets.Combo(140);
            _timeBox.ItemsSource = new List<Choice>
            {
                new Choice(0, "Now"),
                new Choice(6, "+6 hours"),
                new Choice(12, "+12 hours"),
                new Choice(24, "+24 hours"),
                new Choice(48, "+48 hours")
            };
            _timeBox.SelectedIndex = 0;
            _timeBox.SelectionChanged += (s, e) => { if (IsEffectivelyVisible) BeginLoad(false); };

            _refreshButton = Widgets.Button("Refresh", 88);
            _refreshButton.Click += (s, e) => BeginLoad(true);

            _chartsButton = Widgets.Button("Official charts", 130);
            _chartsButton.Click += (s, e) =>
            {
                if (!Widgets.OpenUrl(Endpoints.SpcUpperAirMaps, this))
                {
                    SetStatus("Could not open a browser. Charts are at " + Endpoints.SpcUpperAirMaps, true);
                }
            };

            Children.Add(_levelBox);
            Children.Add(_timeBox);
            Children.Add(_refreshButton);
            Children.Add(_chartsButton);
        }

        public int SelectedLevel
        {
            get
            {
                var choice = _levelBox.SelectedItem as Choice;
                return choice == null ? 250 : choice.Value;
            }
        }

        private int SelectedHoursAhead
        {
            get
            {
                var choice = _timeBox.SelectedItem as Choice;
                return choice == null ? 0 : choice.Value;
            }
        }

        public void Configure(Region region, GeoLocation marker, int level)
        {
            bool regionChanged = _region == null || region == null
                                 || !string.Equals(_region.Id, region.Id, StringComparison.OrdinalIgnoreCase);

            _region = region;
            _marker = marker;

            var choices = _levelBox.ItemsSource as List<Choice>;
            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    if (choices[i].Value == level && _levelBox.SelectedIndex != i)
                    {
                        _levelBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (regionChanged)
            {
                _loaded = false;
                _field = null;
                if (IsEffectivelyVisible) BeginLoad(false);
            }

            InvalidateSurface();
        }

        public override void OnActivated()
        {
            if (!_loaded) BeginLoad(false);
        }

        protected override void LayoutChildren()
        {
            double y = Gutter - 4;
            Place(_levelBox, Gutter, y, 168, 28);
            Place(_timeBox, Gutter + 178, y, 140, 28);
            Place(_refreshButton, Gutter + 328, y, 88, 28);
            Place(_chartsButton, Gutter + 426, y, 130, 28);
        }

        private async void BeginLoad(bool force)
        {
            if (_region == null) return;
            if (force) HttpService.ClearCache();

            CancelPending();
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            SetStatus("Sampling " + SelectedLevel + " hPa winds across " + _region.Title + "...", false);

            try
            {
                JetStreamField field = await _client
                    .GetJetStreamAsync(_region, SelectedLevel, SelectedHoursAhead, token)
                    .ConfigureAwait(true);

                if (token.IsCancellationRequested) return;

                _field = field;
                _loaded = true;

                string valid = field.ValidTime == default(DateTime)
                    ? "unknown time"
                    : field.ValidTime.ToString("ddd d MMM HH:mm") + " UTC";

                SetStatus(field.ModelName + "  ·  valid " + valid
                    + "  ·  peak wind " + Math.Round(field.MaxSpeedKnots).ToString("0") + " kt"
                    + (field.StrongestPoint == null
                        ? string.Empty
                        : " near " + FormatCoordinate(field.StrongestPoint)), false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                SetStatus(ex.Message, true);
            }
        }

        private void SetStatus(string text, bool isError)
        {
            _status = text;
            _statusIsError = isError;
            InvalidateSurface();
        }

        // ---- rendering -------------------------------------------------------

        protected override void DrawSurface(DrawingContext context)
        {
            var status = new Rect(Gutter, ToolbarHeight + 4, Math.Max(10, W - Gutter * 2), StatusHeight);
            AppTheme.DrawLineText(context, _status, AppTheme.Regular, AppTheme.SizeSmall,
                _statusIsError ? AppTheme.Brush(AppTheme.WarningColor) : AppTheme.TextMuted, status);

            Rect host = HostBounds();
            if (host.Width < 40 || host.Height < 40) return;

            AppTheme.DrawCard(context, host);

            if (_field == null || _field.Points.Count == 0)
            {
                AppTheme.DrawText(context, "Upper-air winds have not been loaded yet.", AppTheme.Regular,
                    AppTheme.SizeBody, AppTheme.TextMuted, host, TextAlignment.Center, middle: true);
                return;
            }

            // The map itself is drawn by the shared renderer, so the desktop and
            // phone heads cannot drift apart about where the jet is.
            JetStreamRenderer.Draw(context, host, _field,
                _region == null ? null : _region.DefaultLocations, _marker);
        }

        // ---- geometry --------------------------------------------------------

        /// <summary>The card the map is drawn inside, below the toolbar and status line.</summary>
        private Rect HostBounds()
        {
            double top = ToolbarHeight + StatusHeight + 8;
            return new Rect(Gutter, top, Math.Max(10, W - Gutter * 2), Math.Max(10, H - top - Gutter));
        }

        private static string FormatCoordinate(JetStreamPoint point)
        {
            return Math.Abs(point.Latitude).ToString("0.#") + (point.Latitude >= 0 ? "°N " : "°S ")
                 + Math.Abs(point.Longitude).ToString("0.#") + (point.Longitude >= 0 ? "°E" : "°W");
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        private sealed class Choice
        {
            public Choice(int value, string title)
            {
                Value = value;
                Title = title;
            }

            public int Value { get; private set; }
            public string Title { get; private set; }

            public override string ToString() { return Title; }
        }
    }
}
