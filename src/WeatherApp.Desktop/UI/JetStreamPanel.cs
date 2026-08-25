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
        private const double LegendHeight = 28;

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
                if (!Widgets.OpenUrl(Endpoints.SpcUpperAirMaps))
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

            Rect map = MapBounds(host);
            if (map.Width < 40 || map.Height < 40) return;

            using (context.PushClip(map))
            {
                DrawSpeedField(context, map);
            }

            DrawGraticule(context, map);
            DrawWindArrows(context, map);
            DrawPlaces(context, map);
            DrawJetCoreCallout(context, map);
            DrawLegend(context, host);
        }

        /// <summary>
        /// Fills the map with the interpolated wind-speed field, drawn as small
        /// blocks with bilinear interpolation between grid points. At six pixels a
        /// block the banding is invisible at normal window sizes and the repaint
        /// stays fast.
        /// </summary>
        private void DrawSpeedField(DrawingContext context, Rect map)
        {
            const double Block = 6;

            for (double y = map.Y; y < map.Bottom; y += Block)
            {
                for (double x = map.X; x < map.Right; x += Block)
                {
                    double column = (x - map.X) / map.Width * (_field.Columns - 1);
                    double row = (y - map.Y) / map.Height * (_field.Rows - 1);

                    double speed = InterpolateSpeed(column, row);
                    if (speed < 30d) continue;   // below this there is no jet to show

                    // Opacity ramps with speed so the core stands out from the flow
                    // around it instead of the whole map reading as solid colour.
                    byte alpha = (byte)Math.Min(210d, 40d + (speed - 30d) * 2.2d);

                    context.DrawRectangle(AppTheme.Brush(AppTheme.JetSpeedColor(speed), alpha), null,
                        new Rect(x, y, Math.Min(Block, map.Right - x), Math.Min(Block, map.Bottom - y)));
                }
            }
        }

        /// <summary>Bilinear sample of the speed grid at fractional grid coordinates.</summary>
        private double InterpolateSpeed(double column, double row)
        {
            int c0 = (int)Math.Floor(column);
            int r0 = (int)Math.Floor(row);

            c0 = Math.Max(0, Math.Min(c0, _field.Columns - 1));
            r0 = Math.Max(0, Math.Min(r0, _field.Rows - 1));

            int c1 = Math.Min(c0 + 1, _field.Columns - 1);
            int r1 = Math.Min(r0 + 1, _field.Rows - 1);

            double fc = column - c0;
            double fr = row - r0;

            double top = SpeedAt(c0, r0) + (SpeedAt(c1, r0) - SpeedAt(c0, r0)) * fc;
            double bottom = SpeedAt(c0, r1) + (SpeedAt(c1, r1) - SpeedAt(c0, r1)) * fc;
            return top + (bottom - top) * fr;
        }

        private double SpeedAt(int column, int row)
        {
            JetStreamPoint point = _field.At(column, row);
            return point == null ? 0d : point.SpeedKnots;
        }

        private void DrawGraticule(DrawingContext context, Rect map)
        {
            var pen = new Pen(AppTheme.Brush(AppTheme.TextColor, 46), 1,
                new DashStyle(new double[] { 1, 3 }, 0));

            for (double latitude = Math.Ceiling(_field.SouthLatitude / 10d) * 10d;
                 latitude <= _field.NorthLatitude; latitude += 10d)
            {
                double y = LatitudeToY(latitude, map);
                context.DrawLine(pen, new Point(map.X, y), new Point(map.Right, y));

                AppTheme.DrawLineText(context, latitude.ToString("0") + "°N", AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(map.X + 4, y - 16, 52, 15));
            }

            for (double longitude = Math.Ceiling(_field.WestLongitude / 10d) * 10d;
                 longitude <= _field.EastLongitude; longitude += 10d)
            {
                double x = LongitudeToX(longitude, map);
                context.DrawLine(pen, new Point(x, map.Y), new Point(x, map.Bottom));

                AppTheme.DrawLineText(context, Math.Abs(longitude).ToString("0") + "°W", AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(x + 4, map.Bottom - 18, 54, 15));
            }

            context.DrawRectangle(null, AppTheme.BorderPen, map);
        }

        /// <summary>
        /// One arrow per grid point, pointing the way the wind is blowing.
        /// Meteorological direction is the bearing the wind comes *from*, so the
        /// arrow is drawn along direction + 180 degrees.
        /// </summary>
        private void DrawWindArrows(DrawingContext context, Rect map)
        {
            for (int row = 0; row < _field.Rows; row++)
            {
                for (int column = 0; column < _field.Columns; column++)
                {
                    JetStreamPoint point = _field.At(column, row);
                    if (point == null || point.SpeedKnots < 25d) continue;

                    double x = LongitudeToX(point.Longitude, map);
                    double y = LatitudeToY(point.Latitude, map);
                    if (!map.Contains(new Point(x, y))) continue;

                    double heading = (point.DirectionDegrees + 180d) * Math.PI / 180d;

                    // Length carries speed as well as the colour does, which helps
                    // when the map is printed or read by someone colour-blind.
                    double length = Math.Min(26d, 8d + point.SpeedKnots / 7d);
                    double dx = Math.Sin(heading) * length;
                    double dy = -Math.Cos(heading) * length;

                    var from = new Point(x - dx / 2, y - dy / 2);
                    var to = new Point(x + dx / 2, y + dy / 2);

                    var pen = new Pen(
                        point.IsJetCore ? AppTheme.White : AppTheme.Brush(AppTheme.TextColor, 200),
                        point.IsJetCore ? 2 : 1.3,
                        lineCap: PenLineCap.Round);

                    context.DrawLine(pen, from, to);
                    DrawArrowHead(context, pen, from, to);
                }
            }
        }

        /// <summary>
        /// Avalonia pens have no arrow caps, so the head is two short strokes back
        /// along the shaft at +/- 25 degrees.
        /// </summary>
        private static void DrawArrowHead(DrawingContext context, IPen pen, Point from, Point to)
        {
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            const double HeadLength = 5.5;
            const double Spread = 25 * Math.PI / 180;

            context.DrawLine(pen, to, new Point(
                to.X - HeadLength * Math.Cos(angle - Spread),
                to.Y - HeadLength * Math.Sin(angle - Spread)));

            context.DrawLine(pen, to, new Point(
                to.X - HeadLength * Math.Cos(angle + Spread),
                to.Y - HeadLength * Math.Sin(angle + Spread)));
        }

        /// <summary>Marks known cities so the map has geographic anchors.</summary>
        private void DrawPlaces(DrawingContext context, Rect map)
        {
            var places = new List<GeoLocation>();
            if (_region != null) places.AddRange(_region.DefaultLocations);
            if (_marker != null) places.Add(_marker);

            foreach (GeoLocation place in places)
            {
                if (place.Latitude < _field.SouthLatitude || place.Latitude > _field.NorthLatitude) continue;
                if (place.Longitude < _field.WestLongitude || place.Longitude > _field.EastLongitude) continue;

                double x = LongitudeToX(place.Longitude, map);
                double y = LatitudeToY(place.Latitude, map);

                bool isSelected = _marker != null
                                  && Math.Abs(place.Latitude - _marker.Latitude) < 0.001
                                  && Math.Abs(place.Longitude - _marker.Longitude) < 0.001;

                double radius = isSelected ? 5 : 3;

                context.DrawEllipse(
                    isSelected ? AppTheme.Accent : AppTheme.Brush(Colors.White, 190),
                    new Pen(AppTheme.Brush(Colors.Black, 190)),
                    new Point(x, y), radius, radius);

                if (isSelected || places.Count <= 12)
                {
                    AppTheme.DrawLineText(context, place.Name,
                        isSelected ? AppTheme.Bold : AppTheme.Regular, AppTheme.SizeSmall,
                        isSelected ? AppTheme.Accent : AppTheme.Brush(Colors.White, 210),
                        new Rect(x + 8, y - 9, 120, 18));
                }
            }
        }

        /// <summary>Calls out where the fastest wind in the field is.</summary>
        private void DrawJetCoreCallout(DrawingContext context, Rect map)
        {
            JetStreamPoint core = _field.StrongestPoint;
            if (core == null || core.SpeedKnots < 70d) return;

            double x = LongitudeToX(core.Longitude, map);
            double y = LatitudeToY(core.Latitude, map);

            context.DrawEllipse(null, new Pen(AppTheme.White, 2), new Point(x, y), 11, 11);

            var label = new Rect(x + 15, y - 10, 78, 20);
            context.DrawRectangle(AppTheme.Brush(Colors.Black, 190), null, label, 4, 4);

            AppTheme.DrawLineText(context, Math.Round(core.SpeedKnots).ToString("0") + " kt",
                AppTheme.Bold, AppTheme.SizeSmall, AppTheme.White, label, TextAlignment.Center);
        }

        private static void DrawLegend(DrawingContext context, Rect host)
        {
            int[] thresholds = { 50, 70, 90, 110, 130, 150 };

            double y = host.Bottom - LegendHeight;
            double x = host.X + 14;

            AppTheme.DrawLineText(context, "WIND SPEED", AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextFaint,
                new Rect(x, y, 86, 18));
            x += 90;

            foreach (int threshold in thresholds)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.JetSpeedColor(threshold)), null,
                    new Rect(x, y + 5, 20, 10), 2, 2);

                AppTheme.DrawLineText(context, threshold.ToString(CultureInfo.InvariantCulture),
                    AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextMuted, new Rect(x + 24, y, 32, 18));
                x += 56;
            }

            AppTheme.DrawLineText(context, "kt  ·  jet core is 70 kt and above", AppTheme.Regular,
                AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(x, y, 240, 18));
        }

        // ---- projection ------------------------------------------------------

        private Rect HostBounds()
        {
            double top = ToolbarHeight + StatusHeight + 8;
            return new Rect(Gutter, top, Math.Max(10, W - Gutter * 2), Math.Max(10, H - top - Gutter));
        }

        /// <summary>
        /// The largest rectangle inside the card that keeps the box's aspect ratio,
        /// with longitude compressed by cos(centre latitude) so the region is not
        /// stretched sideways. That matters most for Alaska, where a degree of
        /// longitude is under a third of a degree of latitude.
        /// </summary>
        private Rect MapBounds(Rect host)
        {
            Rect area = host.Deflate(new Thickness(14, 14, 14, LegendHeight + 6));
            if (area.Width <= 0 || area.Height <= 0) return default(Rect);

            double centreLatitude = (_field.NorthLatitude + _field.SouthLatitude) / 2d;
            double spanLatitude = Math.Max(0.1, _field.NorthLatitude - _field.SouthLatitude);
            double spanLongitude = Math.Max(0.1, _field.EastLongitude - _field.WestLongitude)
                                   * Math.Cos(centreLatitude * Math.PI / 180d);

            double aspect = spanLongitude / spanLatitude;

            double width = area.Width;
            double height = width / aspect;

            if (height > area.Height)
            {
                height = area.Height;
                width = height * aspect;
            }

            return new Rect(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                Math.Max(1, width), Math.Max(1, height));
        }

        private double LongitudeToX(double longitude, Rect map)
        {
            double fraction = (longitude - _field.WestLongitude)
                              / (_field.EastLongitude - _field.WestLongitude);
            return map.X + fraction * map.Width;
        }

        private double LatitudeToY(double latitude, Rect map)
        {
            double fraction = (_field.NorthLatitude - latitude)
                              / (_field.NorthLatitude - _field.SouthLatitude);
            return map.Y + fraction * map.Height;
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
