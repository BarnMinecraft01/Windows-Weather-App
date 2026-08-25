using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WeatherApp.Models;
using WeatherApp.Net;
using WeatherApp.Services;
using WeatherApp.UI.Rendering;
using Region = WeatherApp.Models.Region;

namespace WeatherApp.UI
{
    /// <summary>
    /// Radar and the jet stream, sharing one screen behind a two-way toggle.
    ///
    /// The desktop head gives each its own tab. On a phone there is only room for
    /// four bottom-bar destinations, and these two are the "look at a map" case,
    /// so they share one. Both load lazily -- they are the heaviest fetches in the
    /// app and a phone is often on a metered connection.
    /// </summary>
    public sealed class PhoneMapsScreen : WeatherPanel
    {
        private const double Margin = 12;
        private const double ToggleHeight = 40;
        private const double StatusHeight = 20;

        private const int FrameDelayMs = 340;
        private const int LastFrameDelayMs = 1100;

        private readonly RadarService _radar = new RadarService();
        private readonly OpenMeteoClient _openMeteo = new OpenMeteoClient();

        private readonly Button _radarButton;
        private readonly Button _jetButton;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<int, Bitmap> _decoded = new Dictionary<int, Bitmap>();

        private RadarLoop _loop;
        private JetStreamField _field;
        private CancellationTokenSource _cancellation;

        private Region _region;
        private GeoLocation _marker;
        private string _station;
        private int _level = 250;

        private bool _showJet;
        private bool _radarLoaded;
        private bool _jetLoaded;
        private int _frame;
        private string _status = "Loading...";
        private bool _statusIsError;

        public PhoneMapsScreen()
        {
            _radarButton = Widgets.Button("Radar", 100);
            _radarButton.Click += (s, e) => Select(false);

            _jetButton = Widgets.Button("Jet Stream", 120);
            _jetButton.Click += (s, e) => Select(true);

            Children.Add(_radarButton);
            Children.Add(_jetButton);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameDelayMs) };
            _timer.Tick += (s, e) => AdvanceFrame();

            // Tapping the map pauses or resumes the radar loop, which is the only
            // playback control a phone needs.
            PointerReleased += (s, e) =>
            {
                if (_showJet || _loop == null || _loop.Frames.Count < 2) return;

                if (_timer.IsEnabled) _timer.Stop();
                else _timer.Start();

                InvalidateSurface();
            };

            UpdateToggleAppearance();
        }

        public void Configure(Region region, GeoLocation marker, string radarStation, int level)
        {
            bool regionChanged = _region == null || region == null
                                 || !string.Equals(_region.Id, region.Id, StringComparison.OrdinalIgnoreCase);

            _region = region;
            _marker = marker;
            _station = radarStation;
            _level = level;

            if (regionChanged)
            {
                _radarLoaded = false;
                _jetLoaded = false;
                _field = null;
                DiscardDecoded();
                _loop = null;

                if (IsEffectivelyVisible) Load();
            }

            InvalidateSurface();
        }

        public override void OnActivated()
        {
            Load();
        }

        private void Select(bool jet)
        {
            if (_showJet == jet) return;

            _showJet = jet;
            _timer.Stop();
            UpdateToggleAppearance();
            Load();
            InvalidateSurface();
        }

        private void UpdateToggleAppearance()
        {
            _radarButton.Background = _showJet ? AppTheme.SurfaceAltBrush : AppTheme.Accent;
            _jetButton.Background = _showJet ? AppTheme.Accent : AppTheme.SurfaceAltBrush;
        }

        protected override void LayoutChildren()
        {
            Place(_radarButton, Margin, Margin, 100, ToggleHeight - 10);
            Place(_jetButton, Margin + 108, Margin, 120, ToggleHeight - 10);
        }

        // ---- loading ---------------------------------------------------------

        private void Load()
        {
            if (_showJet)
            {
                if (!_jetLoaded) LoadJet();
            }
            else if (!_radarLoaded)
            {
                LoadRadar();
            }
        }

        private async void LoadRadar()
        {
            string sector = !string.IsNullOrEmpty(_station)
                ? _station
                : (_region != null && _region.RadarSectors.Count > 0 ? _region.RadarSectors[0].Code : null);

            if (string.IsNullOrEmpty(sector)) return;

            CancelPending();
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            SetStatus("Loading radar...", false);

            try
            {
                RadarLoop loop = await _radar.GetLoopAsync(sector, token).ConfigureAwait(true);
                if (token.IsCancellationRequested) return;

                DiscardDecoded();
                _loop = loop;
                _radarLoaded = true;
                _frame = Math.Max(0, loop.Frames.Count - 1);

                SetStatus(sector + "  ·  " + loop.Frames.Count + " frames  ·  tap to pause", false);
                if (loop.Frames.Count > 1) _timer.Start();
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                SetStatus(ex.Message, true);
            }
        }

        private async void LoadJet()
        {
            if (_region == null) return;

            CancelPending();
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            SetStatus("Sampling " + _level + " hPa winds...", false);

            try
            {
                JetStreamField field = await _openMeteo
                    .GetJetStreamAsync(_region, _level, 0, token)
                    .ConfigureAwait(true);

                if (token.IsCancellationRequested) return;

                _field = field;
                _jetLoaded = true;

                SetStatus(field.ModelName + "  ·  peak "
                    + Math.Round(field.MaxSpeedKnots).ToString("0") + " kt", false);
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

        private Bitmap FrameImage(int index)
        {
            if (_loop == null || index < 0 || index >= _loop.Frames.Count) return null;

            Bitmap cached;
            if (_decoded.TryGetValue(index, out cached)) return cached;

            byte[] data = _loop.Frames[index].Data;
            if (data == null || data.Length == 0) return null;

            try
            {
                using (var stream = new MemoryStream(data, false))
                {
                    var bitmap = new Bitmap(stream);
                    _decoded[index] = bitmap;
                    return bitmap;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void DiscardDecoded()
        {
            foreach (Bitmap bitmap in _decoded.Values)
            {
                if (bitmap != null) bitmap.Dispose();
            }
            _decoded.Clear();
        }

        private void AdvanceFrame()
        {
            if (_loop == null || _loop.Frames.Count < 2) return;

            _frame++;
            if (_frame >= _loop.Frames.Count) _frame = 0;

            _timer.Interval = TimeSpan.FromMilliseconds(
                _frame == _loop.Frames.Count - 1 ? LastFrameDelayMs : FrameDelayMs);

            InvalidateSurface();
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        // ---- rendering -------------------------------------------------------

        protected override void DrawSurface(DrawingContext context)
        {
            AppTheme.DrawLineText(context, _status, AppTheme.Regular, AppTheme.SizeSmall,
                _statusIsError ? AppTheme.Brush(AppTheme.WarningColor) : AppTheme.TextMuted,
                new Rect(Margin, ToggleHeight + 2, Math.Max(10, W - Margin * 2), StatusHeight));

            double top = ToggleHeight + StatusHeight + 6;
            var host = new Rect(Margin, top, Math.Max(10, W - Margin * 2), Math.Max(10, H - top - Margin));
            if (host.Width < 40 || host.Height < 40) return;

            AppTheme.DrawCard(context, host);

            if (_showJet)
            {
                if (_field == null)
                {
                    AppTheme.DrawText(context, "Upper-air winds have not loaded yet.", AppTheme.Regular,
                        AppTheme.SizeBody, AppTheme.TextMuted, host, TextAlignment.Center, middle: true);
                    return;
                }

                // compact: fewer arrows, no graticule labels, a slimmer legend.
                JetStreamRenderer.Draw(context, host, _field,
                    _region == null ? null : _region.DefaultLocations, _marker, compact: true);
                return;
            }

            if (_loop == null || _loop.Frames.Count == 0)
            {
                AppTheme.DrawText(context, "No radar imagery loaded.", AppTheme.Regular,
                    AppTheme.SizeBody, AppTheme.TextMuted, host, TextAlignment.Center, middle: true);
                return;
            }

            int index = Math.Min(Math.Max(0, _frame), _loop.Frames.Count - 1);
            Bitmap image = FrameImage(index);
            if (image == null) return;

            // Fit without cropping; radar imagery is low resolution and upscaling
            // past 1:1 only invents detail. On a phone that matters more, not less.
            Rect area = host.Deflate(new Thickness(8, 8, 8, 26));
            double scale = Math.Min(area.Width / image.Size.Width, area.Height / image.Size.Height);

            double width = Math.Max(1, image.Size.Width * scale);
            double height = Math.Max(1, image.Size.Height * scale);

            context.DrawImage(image, new Rect(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width, height));

            string caption = "Frame " + (index + 1) + " of " + _loop.Frames.Count
                             + (_timer.IsEnabled ? string.Empty : "  ·  paused");
            AppTheme.DrawLineText(context, caption, AppTheme.Regular, AppTheme.SizeSmall,
                AppTheme.TextFaint, new Rect(host.X + 10, host.Bottom - 24, host.Width - 20, 18));
        }
    }
}
