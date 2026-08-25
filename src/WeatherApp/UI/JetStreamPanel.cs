using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using WeatherApp.Configuration;
using WeatherApp.Models;
// System.Drawing also defines a Region; this file means the geographic one.
using Region = WeatherApp.Models.Region;
using WeatherApp.Net;
using WeatherApp.Services;

namespace WeatherApp.UI
{
    /// <summary>
    /// The jet stream, drawn from gridded 250 hPa (or 300 hPa) winds.
    ///
    /// This is rendered rather than fetched as a picture. Every free upper-air
    /// chart is a static image on a NOAA path that moves every few years, and none
    /// of them let you pick a region or a forecast hour. Sampling the model winds
    /// directly and drawing them means the map covers exactly the box the user
    /// selected, can be stepped forward in time, and keeps working when a chart URL
    /// changes. The hand-analysed charts are still one click away for anyone who
    /// wants them.
    ///
    /// The projection is equirectangular with the horizontal axis scaled by the
    /// cosine of the box's centre latitude. That is not a conformal projection and
    /// it is not trying to be -- it keeps shapes roughly right over a region-sized
    /// box while staying simple enough that the grid maths is obviously correct.
    /// </summary>
    public sealed class JetStreamPanel : WeatherPanel
    {
        private readonly OpenMeteoClient _client = new OpenMeteoClient();

        private readonly ComboBox _levelBox;
        private readonly ComboBox _timeBox;
        private readonly Button _refreshButton;
        private readonly Button _chartsButton;
        private readonly Label _statusLabel;
        private readonly Panel _mapHost;

        private JetStreamField _field;
        private CancellationTokenSource _cancellation;
        private Region _region;
        private GeoLocation _marker;
        private bool _loaded;

        /// <summary>Raised when the user changes the pressure level, so it can be saved.</summary>
        public event EventHandler<int> LevelChanged;

        public JetStreamPanel()
        {
            _levelBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150, Left = 14, Top = 12,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.Text, Font = Theme.FontBody
            };
            _levelBox.Items.Add(new Choice(250, "250 hPa (~34,000 ft)"));
            _levelBox.Items.Add(new Choice(300, "300 hPa (~30,000 ft)"));
            _levelBox.SelectedIndex = 0;
            _levelBox.SelectedIndexChanged += (s, e) =>
            {
                if (LevelChanged != null) LevelChanged(this, SelectedLevel);
                if (Visible) BeginLoad();
            };

            _timeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 130, Left = _levelBox.Right + 10, Top = 12,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.Text, Font = Theme.FontBody
            };
            _timeBox.Items.Add(new Choice(0, "Now"));
            _timeBox.Items.Add(new Choice(6, "+6 hours"));
            _timeBox.Items.Add(new Choice(12, "+12 hours"));
            _timeBox.Items.Add(new Choice(24, "+24 hours"));
            _timeBox.Items.Add(new Choice(48, "+48 hours"));
            _timeBox.SelectedIndex = 0;
            _timeBox.SelectedIndexChanged += (s, e) => { if (Visible) BeginLoad(); };

            _refreshButton = Theme.CreateButton("Refresh", 82);
            _refreshButton.Left = _timeBox.Right + 10;
            _refreshButton.Top = 11;
            _refreshButton.Click += (s, e) => BeginLoad(force: true);

            _chartsButton = Theme.CreateButton("Official charts", 122);
            _chartsButton.Left = _refreshButton.Right + 8;
            _chartsButton.Top = 11;
            _chartsButton.Click += (s, e) => OpenCharts();

            _statusLabel = new Label
            {
                Left = 14, Top = 46, Height = 18, AutoSize = false,
                ForeColor = Theme.TextMuted, Font = Theme.FontSmall,
                Text = "Open this tab to load upper-air winds."
            };

            _mapHost = new Panel { Left = 14, Top = 68, BackColor = Theme.Surface };
            _mapHost.Paint += PaintMap;
            SetDoubleBuffered(_mapHost);

            Controls.Add(_levelBox);
            Controls.Add(_timeBox);
            Controls.Add(_refreshButton);
            Controls.Add(_chartsButton);
            Controls.Add(_statusLabel);
            Controls.Add(_mapHost);

            Resize += (s, e) => LayoutChildren();
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

            for (int i = 0; i < _levelBox.Items.Count; i++)
            {
                if (((Choice)_levelBox.Items[i]).Value == level)
                {
                    if (_levelBox.SelectedIndex != i) _levelBox.SelectedIndex = i;
                    break;
                }
            }

            if (regionChanged)
            {
                _loaded = false;
                _field = null;
                if (Visible) BeginLoad();
            }

            _mapHost.Invalidate();
        }

        public override void OnActivated()
        {
            if (!_loaded) BeginLoad();
        }

        private void LayoutChildren()
        {
            _statusLabel.Width = Math.Max(100, ClientSize.Width - 28);
            _mapHost.Width = Math.Max(50, ClientSize.Width - 28);
            _mapHost.Height = Math.Max(50, ClientSize.Height - _mapHost.Top - 14);
        }

        private async void BeginLoad(bool force = false)
        {
            if (_region == null) return;

            if (force) HttpService.ClearCache();

            CancelPending();
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            _statusLabel.ForeColor = Theme.TextMuted;
            _statusLabel.Text = "Sampling " + SelectedLevel + " hPa winds across " + _region.Title + "...";

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

                _statusLabel.Text = field.ModelName + "  ·  valid " + valid
                    + "  ·  peak wind " + Math.Round(field.MaxSpeedKnots).ToString("0") + " kt"
                    + (field.StrongestPoint == null
                        ? string.Empty
                        : " near " + FormatCoordinate(field.StrongestPoint));

                _mapHost.Invalidate();
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                _statusLabel.ForeColor = Theme.Warning;
                _statusLabel.Text = ex.Message;
                _mapHost.Invalidate();
            }
        }

        // ---- rendering -------------------------------------------------------

        private void PaintMap(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);
            g.Clear(Theme.Surface);

            if (_field == null || _field.Points.Count == 0)
            {
                Theme.DrawText(g, "Upper-air winds have not been loaded yet.",
                    Theme.FontBody, Theme.TextMuted, _mapHost.ClientRectangle,
                    StringAlignment.Center, StringAlignment.Center);
                return;
            }

            Rectangle map = MapBounds();
            if (map.Width < 40 || map.Height < 40) return;

            DrawSpeedField(g, map);
            DrawGraticule(g, map);
            DrawWindArrows(g, map);
            DrawPlaces(g, map);
            DrawJetCoreCallout(g, map);
            DrawLegend(g);
        }

        /// <summary>
        /// Fills the map with the interpolated wind-speed field.
        ///
        /// Drawn as small blocks with bilinear interpolation between grid points
        /// rather than per-pixel: at 6 pixels a block the banding is invisible at
        /// normal window sizes, and it keeps the repaint fast enough to stay smooth
        /// on the older hardware this app targets.
        /// </summary>
        private void DrawSpeedField(Graphics g, Rectangle map)
        {
            const int Block = 6;

            for (int y = map.Y; y < map.Bottom; y += Block)
            {
                for (int x = map.X; x < map.Right; x += Block)
                {
                    double column = (double)(x - map.X) / map.Width * (_field.Columns - 1);
                    double row = (double)(y - map.Y) / map.Height * (_field.Rows - 1);

                    double speed = InterpolateSpeed(column, row);
                    if (speed < 30d) continue;   // below this there is no jet to show

                    Color color = Theme.JetSpeedColor(speed);

                    // Ramp opacity with speed so the core stands out from the flow
                    // around it instead of the whole map reading as solid colour.
                    int alpha = (int)Math.Min(210d, 40d + (speed - 30d) * 2.2d);

                    using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
                    {
                        g.FillRectangle(brush,
                            x, y,
                            Math.Min(Block, map.Right - x),
                            Math.Min(Block, map.Bottom - y));
                    }
                }
            }
        }

        /// <summary>Bilinear sample of the speed grid at fractional grid coordinates.</summary>
        private double InterpolateSpeed(double column, double row)
        {
            int c0 = (int)Math.Floor(column);
            int r0 = (int)Math.Floor(row);
            int c1 = Math.Min(c0 + 1, _field.Columns - 1);
            int r1 = Math.Min(r0 + 1, _field.Rows - 1);

            c0 = Math.Max(0, Math.Min(c0, _field.Columns - 1));
            r0 = Math.Max(0, Math.Min(r0, _field.Rows - 1));

            double fc = column - c0;
            double fr = row - r0;

            double topLeft = SpeedAt(c0, r0);
            double topRight = SpeedAt(c1, r0);
            double bottomLeft = SpeedAt(c0, r1);
            double bottomRight = SpeedAt(c1, r1);

            double top = topLeft + (topRight - topLeft) * fc;
            double bottom = bottomLeft + (bottomRight - bottomLeft) * fc;
            return top + (bottom - top) * fr;
        }

        private double SpeedAt(int column, int row)
        {
            JetStreamPoint point = _field.At(column, row);
            return point == null ? 0d : point.SpeedKnots;
        }

        private void DrawGraticule(Graphics g, Rectangle map)
        {
            using (var pen = new Pen(Color.FromArgb(46, Theme.Text)))
            {
                pen.DashStyle = DashStyle.Dot;

                for (double latitude = Math.Ceiling(_field.SouthLatitude / 10d) * 10d;
                     latitude <= _field.NorthLatitude; latitude += 10d)
                {
                    int y = LatitudeToY(latitude, map);
                    g.DrawLine(pen, map.X, y, map.Right, y);

                    Theme.DrawText(g, latitude.ToString("0") + "°N", Theme.FontSmall, Theme.TextFaint,
                        new Rectangle(map.X + 3, y - 14, 46, 14), wrap: false);
                }

                for (double longitude = Math.Ceiling(_field.WestLongitude / 10d) * 10d;
                     longitude <= _field.EastLongitude; longitude += 10d)
                {
                    int x = LongitudeToX(longitude, map);
                    g.DrawLine(pen, x, map.Y, x, map.Bottom);

                    Theme.DrawText(g, Math.Abs(longitude).ToString("0") + "°W",
                        Theme.FontSmall, Theme.TextFaint,
                        new Rectangle(x + 3, map.Bottom - 16, 48, 14), wrap: false);
                }
            }

            using (var pen = new Pen(Theme.Border))
            {
                g.DrawRectangle(pen, map);
            }
        }

        /// <summary>
        /// One arrow per grid point, pointing the way the wind is blowing.
        ///
        /// Meteorological wind direction is the bearing the wind comes *from*, so
        /// the arrow is drawn along direction + 180 degrees.
        /// </summary>
        private void DrawWindArrows(Graphics g, Rectangle map)
        {
            for (int row = 0; row < _field.Rows; row++)
            {
                for (int column = 0; column < _field.Columns; column++)
                {
                    JetStreamPoint point = _field.At(column, row);
                    if (point == null || point.SpeedKnots < 25d) continue;

                    int x = LongitudeToX(point.Longitude, map);
                    int y = LatitudeToY(point.Latitude, map);
                    if (!map.Contains(x, y)) continue;

                    double heading = (point.DirectionDegrees + 180d) * Math.PI / 180d;

                    // Length carries speed as well as the colour does, which helps
                    // when the map is printed or viewed by someone colour-blind.
                    double length = Math.Min(26d, 8d + point.SpeedKnots / 7d);

                    double dx = Math.Sin(heading) * length;
                    double dy = -Math.Cos(heading) * length;

                    var from = new PointF((float)(x - dx / 2), (float)(y - dy / 2));
                    var to = new PointF((float)(x + dx / 2), (float)(y + dy / 2));

                    Color color = point.IsJetCore ? Color.White : Color.FromArgb(200, Theme.Text);

                    using (var pen = new Pen(color, point.IsJetCore ? 2f : 1.3f))
                    {
                        pen.EndCap = LineCap.ArrowAnchor;
                        pen.StartCap = LineCap.Round;
                        g.DrawLine(pen, from, to);
                    }
                }
            }
        }

        /// <summary>Marks known cities so the map has geographic anchors.</summary>
        private void DrawPlaces(Graphics g, Rectangle map)
        {
            var places = new List<GeoLocation>();
            if (_region != null) places.AddRange(_region.DefaultLocations);
            if (_marker != null) places.Add(_marker);

            foreach (GeoLocation place in places)
            {
                if (place.Latitude < _field.SouthLatitude || place.Latitude > _field.NorthLatitude) continue;
                if (place.Longitude < _field.WestLongitude || place.Longitude > _field.EastLongitude) continue;

                int x = LongitudeToX(place.Longitude, map);
                int y = LatitudeToY(place.Latitude, map);

                bool isSelected = _marker != null
                                  && Math.Abs(place.Latitude - _marker.Latitude) < 0.001
                                  && Math.Abs(place.Longitude - _marker.Longitude) < 0.001;

                int radius = isSelected ? 5 : 3;
                Color fill = isSelected ? Theme.Accent : Color.FromArgb(190, Color.White);

                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(Color.FromArgb(190, Color.Black)))
                {
                    g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                }

                if (isSelected || places.Count <= 12)
                {
                    Theme.DrawText(g, place.Name, isSelected ? Theme.FontSmallBold : Theme.FontSmall,
                        isSelected ? Theme.Accent : Color.FromArgb(210, Color.White),
                        new Rectangle(x + 6, y - 8, 110, 16), wrap: false);
                }
            }
        }

        /// <summary>Calls out where the fastest wind in the field is.</summary>
        private void DrawJetCoreCallout(Graphics g, Rectangle map)
        {
            JetStreamPoint core = _field.StrongestPoint;
            if (core == null || core.SpeedKnots < 70d) return;

            int x = LongitudeToX(core.Longitude, map);
            int y = LatitudeToY(core.Latitude, map);

            using (var pen = new Pen(Color.White, 2f))
            {
                g.DrawEllipse(pen, x - 11, y - 11, 22, 22);
            }

            string label = Math.Round(core.SpeedKnots).ToString("0") + " kt";
            var bounds = new Rectangle(x + 14, y - 10, 74, 18);

            using (var brush = new SolidBrush(Color.FromArgb(190, Color.Black)))
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 4))
            {
                g.FillPath(brush, path);
            }

            Theme.DrawText(g, label, Theme.FontSmallBold, Color.White, bounds,
                StringAlignment.Center, StringAlignment.Center, false);
        }

        private void DrawLegend(Graphics g)
        {
            int[] thresholds = { 50, 70, 90, 110, 130, 150 };

            int y = _mapHost.ClientSize.Height - 24;
            int x = 12;

            Theme.DrawText(g, "WIND SPEED", Theme.FontSmall, Theme.TextFaint,
                new Rectangle(x, y, 78, 16), StringAlignment.Near, StringAlignment.Center, false);
            x += 82;

            foreach (int threshold in thresholds)
            {
                using (var brush = new SolidBrush(Theme.JetSpeedColor(threshold)))
                {
                    g.FillRectangle(brush, x, y + 4, 18, 9);
                }

                Theme.DrawText(g, threshold.ToString(CultureInfo.InvariantCulture),
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(x + 20, y, 30, 16), StringAlignment.Near, StringAlignment.Center, false);

                x += 50;
            }

            Theme.DrawText(g, "kt  ·  jet core is 70 kt and above",
                Theme.FontSmall, Theme.TextFaint,
                new Rectangle(x, y, 220, 16), StringAlignment.Near, StringAlignment.Center, false);
        }

        // ---- projection ------------------------------------------------------

        /// <summary>
        /// The largest rectangle inside the host that keeps the box's aspect ratio,
        /// with longitude compressed by cos(centre latitude) so the region is not
        /// stretched sideways -- which matters most for Alaska, where a degree of
        /// longitude is under a third of a degree of latitude.
        /// </summary>
        private Rectangle MapBounds()
        {
            Rectangle host = Rectangle.Inflate(_mapHost.ClientRectangle, -12, -12);
            host.Height -= 26;   // room for the legend strip
            if (host.Width <= 0 || host.Height <= 0) return Rectangle.Empty;

            double centreLatitude = (_field.NorthLatitude + _field.SouthLatitude) / 2d;
            double spanLatitude = Math.Max(0.1, _field.NorthLatitude - _field.SouthLatitude);
            double spanLongitude = Math.Max(0.1, _field.EastLongitude - _field.WestLongitude)
                                   * Math.Cos(centreLatitude * Math.PI / 180d);

            double aspect = spanLongitude / spanLatitude;

            int width = host.Width;
            int height = (int)(width / aspect);

            if (height > host.Height)
            {
                height = host.Height;
                width = (int)(height * aspect);
            }

            return new Rectangle(
                host.X + (host.Width - width) / 2,
                host.Y + (host.Height - height) / 2,
                Math.Max(1, width), Math.Max(1, height));
        }

        private int LongitudeToX(double longitude, Rectangle map)
        {
            double fraction = (longitude - _field.WestLongitude)
                              / (_field.EastLongitude - _field.WestLongitude);
            return map.X + (int)Math.Round(fraction * map.Width);
        }

        private int LatitudeToY(double latitude, Rectangle map)
        {
            double fraction = (_field.NorthLatitude - latitude)
                              / (_field.NorthLatitude - _field.SouthLatitude);
            return map.Y + (int)Math.Round(fraction * map.Height);
        }

        private static string FormatCoordinate(JetStreamPoint point)
        {
            return Math.Abs(point.Latitude).ToString("0.#") + (point.Latitude >= 0 ? "°N " : "°S ")
                 + Math.Abs(point.Longitude).ToString("0.#") + (point.Longitude >= 0 ? "°E" : "°W");
        }

        private void OpenCharts()
        {
            try
            {
                Process.Start(Endpoints.SpcUpperAirMaps);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this,
                    "Could not open a browser. The hand-analysed upper-air charts are at:"
                    + Environment.NewLine + Endpoints.SpcUpperAirMaps,
                    "Upper-air charts", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        private static void SetDoubleBuffered(Control control)
        {
            var property = typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (property != null) property.SetValue(control, true, null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CancelPending();
            base.Dispose(disposing);
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
