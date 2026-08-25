using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WeatherApp.Models;
using WeatherApp.Net;
using WeatherApp.Services;
using Region = WeatherApp.Models.Region;

namespace WeatherApp.UI
{
    /// <summary>
    /// Animated NWS radar.
    ///
    /// The scrub bar is drawn and hit-tested by this panel rather than being a
    /// Slider. That keeps it visually consistent with everything else on the
    /// surface, and it is barely more code than styling a stock control would be.
    /// </summary>
    public sealed class RadarPanel : WeatherPanel
    {
        private const double Gutter = 16;
        private const double ToolbarHeight = 44;
        private const double StatusHeight = 22;
        private const double ScrubHeight = 26;

        private const int FrameDelayMs = 320;
        private const int LastFrameDelayMs = 1100;

        private readonly RadarService _radar = new RadarService();
        private readonly ComboBox _sectorBox;
        private readonly Button _playButton;
        private readonly Button _refreshButton;
        private readonly Button _browserButton;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<int, Bitmap> _decoded = new Dictionary<int, Bitmap>();

        private RadarLoop _loop;
        private CancellationTokenSource _cancellation;
        private Region _region;
        private string _status = "Open this tab to load radar imagery.";
        private bool _statusIsError;
        private int _frame;
        private bool _loaded;
        private bool _scrubbing;

        public RadarPanel()
        {
            _sectorBox = Widgets.Combo(230);
            _sectorBox.SelectionChanged += (s, e) => { if (IsEffectivelyVisible) BeginLoad(false); };

            _playButton = Widgets.Button("Pause", 84);
            _playButton.Click += (s, e) => TogglePlayback();

            _refreshButton = Widgets.Button("Refresh", 88);
            _refreshButton.Click += (s, e) => BeginLoad(true);

            _browserButton = Widgets.Button("Open full radar", 136);
            _browserButton.Click += (s, e) => OpenInBrowser();

            Children.Add(_sectorBox);
            Children.Add(_playButton);
            Children.Add(_refreshButton);
            Children.Add(_browserButton);

            // Created on the UI thread: Avalonia 12 binds a DispatcherTimer to the
            // dispatcher current at construction time.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameDelayMs) };
            _timer.Tick += (s, e) => AdvanceFrame();

            PointerPressed += (s, e) =>
            {
                if (!ScrubBounds().Contains(e.GetPosition(this))) return;

                _scrubbing = true;
                StopPlayback();
                ScrubTo(e.GetPosition(this).X);
            };
            PointerMoved += (s, e) =>
            {
                if (_scrubbing) ScrubTo(e.GetPosition(this).X);
            };
            PointerReleased += (s, e) => _scrubbing = false;
        }

        /// <summary>Rebuilds the sector list for a region and the location's own NEXRAD site.</summary>
        public void Configure(Region region, string localStation, string preferredSector)
        {
            _region = region;

            string previous = preferredSector;
            var current = _sectorBox.SelectedItem as SectorEntry;
            if (current != null) previous = current.Code;

            var entries = new List<SectorEntry>();
            if (!string.IsNullOrEmpty(localStation))
            {
                entries.Add(new SectorEntry(localStation, "Local radar (" + localStation + ")", null));
            }
            if (region != null)
            {
                foreach (RadarSector sector in region.RadarSectors)
                {
                    entries.Add(new SectorEntry(sector.Code, sector.Title, sector.BrowserSlug));
                }
            }

            int selected = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Code, previous, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                    break;
                }
            }

            _sectorBox.ItemsSource = entries;
            if (entries.Count > 0) _sectorBox.SelectedIndex = selected;
        }

        public string SelectedSector
        {
            get
            {
                var entry = _sectorBox.SelectedItem as SectorEntry;
                return entry == null ? string.Empty : entry.Code;
            }
        }

        public override void OnActivated()
        {
            // Radar frames are the heaviest thing the app downloads, so the tab only
            // fetches them once the user actually looks at it.
            if (!_loaded) BeginLoad(false);
        }

        protected override void LayoutChildren()
        {
            double y = Gutter - 4;
            Place(_sectorBox, Gutter, y, 230, 28);
            Place(_playButton, Gutter + 240, y, 84, 28);
            Place(_refreshButton, Gutter + 332, y, 88, 28);
            Place(_browserButton, Gutter + 428, y, 136, 28);
        }

        // ---- loading ---------------------------------------------------------

        private async void BeginLoad(bool force)
        {
            var entry = _sectorBox.SelectedItem as SectorEntry;
            if (entry == null) return;

            if (force) HttpService.ClearCache();

            StopPlayback();
            CancelPending();

            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            SetStatus("Loading " + entry.Title + "...", false);

            try
            {
                RadarLoop loop = await _radar.GetLoopAsync(entry.Code, token).ConfigureAwait(true);
                if (token.IsCancellationRequested) return;

                DiscardDecoded();
                _loop = loop;
                _loaded = true;
                _frame = Math.Max(0, loop.Frames.Count - 1);

                SetStatus(loop.IsAnimatedGif
                    ? entry.Title + " -- individual frames were unavailable, showing the animated loop."
                    : entry.Title + " -- " + loop.Frames.Count + " frames, updated "
                      + loop.RetrievedAt.ToString("h:mm tt"), false);

                InvalidateVisual();
                if (loop.Frames.Count > 1) StartPlayback();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request.
            }
            catch (WeatherServiceException ex)
            {
                SetStatus(ex.Message, true);
                InvalidateVisual();
            }
        }

        private void SetStatus(string text, bool isError)
        {
            _status = text;
            _statusIsError = isError;
            InvalidateVisual();
        }

        /// <summary>
        /// Decodes a frame to an Avalonia bitmap, caching the result. The core hands
        /// back raw bytes so it stays platform-neutral, which makes decoding this
        /// head's job; caching matters because the timer repaints several times a
        /// second.
        /// </summary>
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
                // A frame that will not decode is dropped rather than faulting the paint.
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

        // ---- playback --------------------------------------------------------

        private void AdvanceFrame()
        {
            if (_loop == null || _loop.Frames.Count < 2) return;

            _frame++;
            if (_frame >= _loop.Frames.Count) _frame = 0;

            // Hold on the newest frame so the eye can settle before the loop restarts.
            _timer.Interval = TimeSpan.FromMilliseconds(
                _frame == _loop.Frames.Count - 1 ? LastFrameDelayMs : FrameDelayMs);

            InvalidateVisual();
        }

        private void TogglePlayback()
        {
            if (_timer.IsEnabled) StopPlayback();
            else StartPlayback();
        }

        private void StartPlayback()
        {
            if (_loop == null || _loop.Frames.Count < 2) return;

            _timer.Start();
            _playButton.Content = "Pause";
        }

        private void StopPlayback()
        {
            _timer.Stop();
            _playButton.Content = "Play";
        }

        private void ScrubTo(double x)
        {
            if (_loop == null || _loop.Frames.Count == 0) return;

            Rect track = ScrubBounds();
            double fraction = (x - track.X) / Math.Max(1, track.Width);
            fraction = Math.Min(1, Math.Max(0, fraction));

            int frame = (int)Math.Round(fraction * (_loop.Frames.Count - 1));
            if (frame == _frame) return;

            _frame = frame;
            InvalidateVisual();
        }

        // ---- rendering -------------------------------------------------------

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var status = new Rect(Gutter, ToolbarHeight + 4, Math.Max(10, W - Gutter * 2), StatusHeight);
            Theme.DrawLineText(context, _status, Theme.Regular, Theme.SizeSmall,
                _statusIsError ? Theme.Brush(Theme.WarningColor) : Theme.TextMuted, status);

            Rect host = ImageBounds();
            if (host.Width < 20 || host.Height < 20) return;

            Theme.DrawCard(context, host);

            if (_loop == null || _loop.Frames.Count == 0)
            {
                Theme.DrawText(context, "No radar imagery loaded.", Theme.Regular, Theme.SizeBody,
                    Theme.TextMuted, host, TextAlignment.Center, middle: true);
                return;
            }

            int index = Math.Min(Math.Max(0, _frame), _loop.Frames.Count - 1);
            Bitmap image = FrameImage(index);

            if (image != null)
            {
                // Fit without cropping and without enlarging past 1:1; radar imagery
                // is already low resolution and upscaling it invents detail.
                Rect area = host.Deflate(new Thickness(10, 10, 10, ScrubHeight + 10));
                double scale = Math.Min(area.Width / image.Size.Width, area.Height / image.Size.Height);
                if (scale > 1d) scale = 1d;

                double width = Math.Max(1, image.Size.Width * scale);
                double height = Math.Max(1, image.Size.Height * scale);

                var target = new Rect(
                    area.X + (area.Width - width) / 2,
                    area.Y + (area.Height - height) / 2,
                    width, height);

                context.DrawImage(image, target);
            }

            DrawScrubber(context, index);
        }

        private void DrawScrubber(DrawingContext context, int index)
        {
            Rect track = ScrubBounds();
            if (track.Width < 40) return;

            context.DrawRectangle(Theme.Brush(Theme.BorderColor, 110), null,
                new Rect(track.X, track.Y + track.Height / 2 - 2, track.Width, 4), 2, 2);

            int count = _loop.Frames.Count;
            for (int i = 0; i < count; i++)
            {
                double x = track.X + (count == 1 ? 0 : track.Width * i / (count - 1));
                bool isCurrent = i == index;

                context.DrawEllipse(
                    isCurrent ? Theme.Accent : Theme.Brush(Theme.TextFaintColor),
                    null,
                    new Point(x, track.Y + track.Height / 2),
                    isCurrent ? 6 : 3,
                    isCurrent ? 6 : 3);
            }

            string caption = "Frame " + (index + 1) + " of " + count
                             + (index == count - 1 ? "  (latest)" : string.Empty);
            Theme.DrawLineText(context, caption, Theme.Regular, Theme.SizeSmall, Theme.TextFaint,
                new Rect(track.X, track.Bottom - 2, track.Width, 16), TextAlignment.Right);
        }

        private Rect ImageBounds()
        {
            double top = ToolbarHeight + StatusHeight + 8;
            return new Rect(Gutter, top, Math.Max(10, W - Gutter * 2), Math.Max(10, H - top - Gutter));
        }

        private Rect ScrubBounds()
        {
            Rect host = ImageBounds();
            return new Rect(host.X + 20, host.Bottom - ScrubHeight - 6, Math.Max(10, host.Width - 40), ScrubHeight);
        }

        private void OpenInBrowser()
        {
            var entry = _sectorBox.SelectedItem as SectorEntry;
            if (entry == null) return;

            string url = string.IsNullOrEmpty(entry.BrowserSlug)
                ? "https://radar.weather.gov/station/" + entry.Code.ToLowerInvariant() + "/standard"
                : RadarService.BrowserUrl(entry.BrowserSlug);

            if (!Widgets.OpenUrl(url)) SetStatus("Could not open a browser. The address is " + url, true);
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        private sealed class SectorEntry
        {
            public SectorEntry(string code, string title, string browserSlug)
            {
                Code = code;
                Title = title;
                BrowserSlug = browserSlug;
            }

            public string Code { get; private set; }
            public string Title { get; private set; }
            public string BrowserSlug { get; private set; }

            public override string ToString() { return Title; }
        }
    }
}
