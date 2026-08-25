using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
// System.Threading.Timer is also in scope here; the UI one is what is wanted.
using Timer = System.Windows.Forms.Timer;
using WeatherApp.Models;
// System.Drawing also defines a Region; this file means the geographic one.
using Region = WeatherApp.Models.Region;
using WeatherApp.Net;
using WeatherApp.Services;

namespace WeatherApp.UI
{
    /// <summary>
    /// Animated NWS radar.
    ///
    /// The ten RIDGE II frames are fetched individually rather than using the
    /// ready-made animated GIF, which is what makes the scrub bar and the pause
    /// button possible: being able to stop on the frame you want and step back
    /// through it is the whole reason to look at a radar loop. The nearest NEXRAD
    /// site for the selected location is offered alongside the regional mosaics,
    /// because "is it raining on my street" and "where is the line of storms" are
    /// different questions.
    /// </summary>
    public sealed class RadarPanel : WeatherPanel
    {
        private readonly RadarService _radar = new RadarService();

        private readonly ComboBox _sectorBox;
        private readonly Button _playButton;
        private readonly Button _refreshButton;
        private readonly Button _browserButton;
        private readonly TrackBar _frameBar;
        private readonly Label _statusLabel;
        private readonly Panel _imageHost;
        private readonly Timer _animationTimer;

        private RadarLoop _loop;
        private readonly Dictionary<int, Image> _decoded = new Dictionary<int, Image>();
        private CancellationTokenSource _cancellation;
        private Region _region;
        private string _localStation;
        private bool _loaded;

        /// <summary>Frame dwell in milliseconds; the last frame is held longer.</summary>
        private const int FrameDelayMs = 320;
        private const int LastFrameDelayMs = 1100;

        public RadarPanel()
        {
            _sectorBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 220,
                Left = 14,
                Top = 12,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                Font = Theme.FontBody
            };
            // Only auto-load once the tab is on screen; Configure() populates this
            // list on every refresh and must not trigger a download behind the scenes.
            _sectorBox.SelectedIndexChanged += (s, e) => { if (Visible) BeginLoad(); };

            _playButton = Theme.CreateButton("Pause", 78);
            _playButton.Left = _sectorBox.Right + 10;
            _playButton.Top = 11;
            _playButton.Click += (s, e) => TogglePlayback();

            _refreshButton = Theme.CreateButton("Refresh", 82);
            _refreshButton.Left = _playButton.Right + 8;
            _refreshButton.Top = 11;
            _refreshButton.Click += (s, e) => BeginLoad(force: true);

            _browserButton = Theme.CreateButton("Open full radar", 128);
            _browserButton.Left = _refreshButton.Right + 8;
            _browserButton.Top = 11;
            _browserButton.Click += (s, e) => OpenInBrowser();

            _frameBar = new TrackBar
            {
                Left = _browserButton.Right + 14,
                Top = 12,
                Width = 200,
                Minimum = 0,
                Maximum = 9,
                TickStyle = TickStyle.None,
                BackColor = Theme.Background
            };
            _frameBar.ValueChanged += OnFrameBarChanged;

            _statusLabel = new Label
            {
                Left = 14,
                Top = 46,
                AutoSize = false,
                Height = 18,
                ForeColor = Theme.TextMuted,
                Font = Theme.FontSmall,
                Text = "Select a radar view."
            };

            _imageHost = new Panel { Left = 14, Top = 68, BackColor = Theme.Surface };
            _imageHost.Paint += PaintFrame;
            SetDoubleBuffered(_imageHost);

            _animationTimer = new Timer { Interval = FrameDelayMs };
            _animationTimer.Tick += (s, e) => AdvanceFrame();

            Controls.Add(_sectorBox);
            Controls.Add(_playButton);
            Controls.Add(_refreshButton);
            Controls.Add(_browserButton);
            Controls.Add(_frameBar);
            Controls.Add(_statusLabel);
            Controls.Add(_imageHost);

            Resize += (s, e) => LayoutChildren();
        }

        /// <summary>Rebuilds the sector list for a region and the location's own NEXRAD site.</summary>
        public void Configure(Region region, string localStation, string preferredSector)
        {
            _region = region;
            _localStation = localStation;

            string previous = preferredSector;
            if (_sectorBox.SelectedItem != null)
            {
                previous = ((SectorEntry)_sectorBox.SelectedItem).Code;
            }

            _sectorBox.BeginUpdate();
            _sectorBox.Items.Clear();

            if (!string.IsNullOrEmpty(localStation))
            {
                _sectorBox.Items.Add(new SectorEntry(
                    localStation, "Local radar (" + localStation + ")", null));
            }

            if (region != null)
            {
                foreach (RadarSector sector in region.RadarSectors)
                {
                    _sectorBox.Items.Add(new SectorEntry(sector.Code, sector.Title, sector.BrowserSlug));
                }
            }

            int selected = 0;
            for (int i = 0; i < _sectorBox.Items.Count; i++)
            {
                if (string.Equals(((SectorEntry)_sectorBox.Items[i]).Code, previous,
                        StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                    break;
                }
            }

            if (_sectorBox.Items.Count > 0) _sectorBox.SelectedIndex = selected;
            _sectorBox.EndUpdate();
        }

        /// <summary>The sector currently on screen, so it can be remembered between runs.</summary>
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
            if (!_loaded) BeginLoad();
        }

        private void LayoutChildren()
        {
            _statusLabel.Width = Math.Max(100, ClientSize.Width - 28);
            _imageHost.Width = Math.Max(50, ClientSize.Width - 28);
            _imageHost.Height = Math.Max(50, ClientSize.Height - _imageHost.Top - 14);
        }

        private async void BeginLoad(bool force = false)
        {
            var entry = _sectorBox.SelectedItem as SectorEntry;
            if (entry == null) return;

            if (force) HttpService.ClearCache();

            StopPlayback();
            CancelPending();

            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            _statusLabel.ForeColor = Theme.TextMuted;
            _statusLabel.Text = "Loading " + entry.Title + "...";

            try
            {
                RadarLoop loop = await _radar.GetLoopAsync(entry.Code, token).ConfigureAwait(true);
                if (token.IsCancellationRequested) return;

                DiscardDecodedFrames();
                _loop = loop;
                _loaded = true;

                _frameBar.Maximum = Math.Max(0, loop.Frames.Count - 1);
                _frameBar.Value = _frameBar.Maximum;
                _frameBar.Enabled = loop.Frames.Count > 1;

                _statusLabel.Text = loop.IsAnimatedGif
                    ? entry.Title + " -- individual frames were unavailable, showing the animated loop."
                    : entry.Title + " -- " + loop.Frames.Count + " frames, updated "
                      + loop.RetrievedAt.ToString("h:mm tt");

                _imageHost.Invalidate();
                if (loop.Frames.Count > 1) StartPlayback();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request.
            }
            catch (WeatherServiceException ex)
            {
                _statusLabel.ForeColor = Theme.Warning;
                _statusLabel.Text = ex.Message;
                _imageHost.Invalidate();
            }
        }

        private void PaintFrame(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PrepareGraphics(g);
            g.Clear(Theme.Surface);

            if (_loop == null || _loop.Frames.Count == 0)
            {
                Theme.DrawText(g, "No radar imagery loaded.", Theme.FontBody, Theme.TextMuted,
                    _imageHost.ClientRectangle, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            int index = Math.Min(Math.Max(0, _frameBar.Value), _loop.Frames.Count - 1);
            Image image = FrameImage(index);
            if (image == null) return;

            // Fit without cropping and without enlarging past 1:1; radar imagery is
            // already low resolution and upscaling it invents detail.
            Rectangle host = _imageHost.ClientRectangle;
            double scale = Math.Min((double)host.Width / image.Width, (double)host.Height / image.Height);
            if (scale > 1d) scale = 1d;

            int width = Math.Max(1, (int)(image.Width * scale));
            int height = Math.Max(1, (int)(image.Height * scale));

            var target = new Rectangle(
                host.X + (host.Width - width) / 2,
                host.Y + (host.Height - height) / 2,
                width, height);

            g.DrawImage(image, target);

            string caption = "Frame " + (index + 1) + " of " + _loop.Frames.Count
                             + (index == _loop.Frames.Count - 1 ? "  (latest)" : string.Empty);
            Theme.DrawText(g, caption, Theme.FontSmall, Theme.TextFaint,
                new Rectangle(host.X + 8, host.Bottom - 20, host.Width - 16, 16), wrap: false);
        }

        /// <summary>
        /// Decodes a frame to a GDI+ image, caching the result.
        ///
        /// The core now hands back raw bytes so that it stays free of System.Drawing,
        /// which means decoding is this head's job. The cache matters: the animation
        /// timer repaints several times a second and decoding a GIF on every paint
        /// would be visible on the older hardware this build targets.
        ///
        /// The MemoryStream is deliberately left undisposed -- GDI+ reads from it
        /// lazily for the lifetime of the Image, and closing it here would fault
        /// later during painting. It holds no unmanaged handle.
        /// </summary>
        private Image FrameImage(int index)
        {
            if (_loop == null || index < 0 || index >= _loop.Frames.Count) return null;

            Image cached;
            if (_decoded.TryGetValue(index, out cached)) return cached;

            byte[] data = _loop.Frames[index].Data;
            if (data == null || data.Length == 0) return null;

            try
            {
                Image image = Image.FromStream(new MemoryStream(data, false));
                _decoded[index] = image;
                return image;
            }
            catch (ArgumentException)
            {
                return null;      // not an image after all
            }
            catch (OutOfMemoryException)
            {
                return null;      // how GDI+ reports an unrecognised format
            }
        }

        private void DiscardDecodedFrames()
        {
            foreach (Image image in _decoded.Values)
            {
                if (image != null) image.Dispose();
            }
            _decoded.Clear();
        }

        private void AdvanceFrame()
        {
            if (_loop == null || _loop.Frames.Count < 2) return;

            int next = _frameBar.Value + 1;
            if (next > _frameBar.Maximum) next = _frameBar.Minimum;

            // Hold on the newest frame so the eye can settle before the loop restarts.
            _animationTimer.Interval = next == _frameBar.Maximum ? LastFrameDelayMs : FrameDelayMs;

            _frameBar.ValueChanged -= OnFrameBarChanged;
            _frameBar.Value = next;
            _frameBar.ValueChanged += OnFrameBarChanged;

            _imageHost.Invalidate();
        }

        private void OnFrameBarChanged(object sender, EventArgs e)
        {
            StopPlayback();
            _imageHost.Invalidate();
        }

        private void TogglePlayback()
        {
            if (_animationTimer.Enabled) StopPlayback();
            else StartPlayback();
        }

        private void StartPlayback()
        {
            if (_loop == null || _loop.Frames.Count < 2) return;

            _animationTimer.Start();
            _playButton.Text = "Pause";
        }

        private void StopPlayback()
        {
            _animationTimer.Stop();
            _playButton.Text = "Play";
        }

        private void OpenInBrowser()
        {
            var entry = _sectorBox.SelectedItem as SectorEntry;
            if (entry == null) return;

            string url = string.IsNullOrEmpty(entry.BrowserSlug)
                ? "https://radar.weather.gov/station/" + entry.Code.ToLowerInvariant() + "/standard"
                : RadarService.BrowserUrl(entry.BrowserSlug);

            try
            {
                // UseShellExecute must be set explicitly: it defaults to false on
                // .NET Core, where passing a bare URL then throws.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this, "Could not open a browser. The address is:" + Environment.NewLine + url,
                    "Open radar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void CancelPending()
        {
            if (_cancellation == null) return;

            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        /// <summary>Panel does not expose DoubleBuffered, so it is set reflectively.</summary>
        private static void SetDoubleBuffered(Control control)
        {
            var property = typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (property != null) property.SetValue(control, true, null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPending();
                _animationTimer.Stop();
                _animationTimer.Dispose();
                DiscardDecodedFrames();
            }
            base.Dispose(disposing);
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
