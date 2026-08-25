using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using WeatherApp.Models;
using WeatherApp.Net;
using WeatherApp.Services;

namespace WeatherApp.UI
{
    /// <summary>
    /// Search dialog for adding a place.
    ///
    /// Accepts a town name, a US ZIP code, or a raw "latitude, longitude" pair in
    /// the same box, because people reach for all three and making them pick a mode
    /// first is friction for no benefit.
    /// </summary>
    public sealed class LocationPickerForm : Form
    {
        private readonly GeocodingClient _geocoder = new GeocodingClient();
        private readonly TextBox _searchBox;
        private readonly ListBox _results;
        private readonly Button _okButton;
        private readonly Label _hintLabel;

        private readonly List<GeoLocation> _matches = new List<GeoLocation>();
        private CancellationTokenSource _cancellation;

        public GeoLocation SelectedLocation { get; private set; }

        public LocationPickerForm()
        {
            Text = "Add a location";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(430, 340);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;

            var prompt = new Label
            {
                Text = "Town, ZIP code, or latitude and longitude:",
                Left = 14, Top = 14, Width = 400, Height = 18,
                ForeColor = Theme.TextMuted
            };

            _searchBox = new TextBox
            {
                Left = 14, Top = 36, Width = 300,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;

                e.SuppressKeyPress = true;
                BeginSearch();
            };

            Button searchButton = Theme.CreateButton("Search", 100);
            searchButton.Left = _searchBox.Right + 8;
            searchButton.Top = 35;
            searchButton.Click += (s, e) => BeginSearch();

            _results = new ListBox
            {
                Left = 14, Top = 70, Width = 400, Height = 200,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _results.SelectedIndexChanged += (s, e) => _okButton.Enabled = _results.SelectedIndex >= 0;
            _results.DoubleClick += (s, e) => Accept();

            _hintLabel = new Label
            {
                Left = 14, Top = 276, Width = 400, Height = 18,
                ForeColor = Theme.TextFaint,
                Font = Theme.FontSmall,
                Text = "Places inside National Weather Service coverage are listed first."
            };

            _okButton = Theme.CreateButton("Add", 100);
            _okButton.Left = 214;
            _okButton.Top = 300;
            _okButton.Enabled = false;
            _okButton.Click += (s, e) => Accept();

            Button cancelButton = Theme.CreateButton("Cancel", 100);
            cancelButton.Left = 320;
            cancelButton.Top = 300;
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(prompt);
            Controls.Add(_searchBox);
            Controls.Add(searchButton);
            Controls.Add(_results);
            Controls.Add(_hintLabel);
            Controls.Add(_okButton);
            Controls.Add(cancelButton);

            AcceptButton = _okButton;
            CancelButton = cancelButton;
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

            _hintLabel.ForeColor = Theme.TextFaint;
            _hintLabel.Text = "Searching...";
            _results.Items.Clear();
            _matches.Clear();

            try
            {
                List<GeoLocation> found = await _geocoder
                    .SearchAsync(query, _cancellation.Token)
                    .ConfigureAwait(true);

                foreach (GeoLocation location in found)
                {
                    _matches.Add(location);

                    string label = location.DisplayName;
                    if (!string.IsNullOrWhiteSpace(location.Country) && !location.IsNwsCovered)
                    {
                        label += " (" + location.Country + ")";
                    }
                    if (string.IsNullOrWhiteSpace(location.Name)) label = location.Coordinates;

                    _results.Items.Add(label + "   —   " + location.Coordinates);
                }

                if (_matches.Count == 0)
                {
                    _hintLabel.Text = "Nothing matched \"" + query.Trim() + "\". Try a nearby larger town.";
                }
                else
                {
                    _results.SelectedIndex = 0;
                    _hintLabel.Text = _matches.Count == 1
                        ? "1 match."
                        : _matches.Count + " matches. Places inside NWS coverage are listed first.";
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WeatherServiceException ex)
            {
                _hintLabel.ForeColor = Theme.Warning;
                _hintLabel.Text = ex.Message;
            }
        }

        private void Accept()
        {
            int index = _results.SelectedIndex;
            if (index < 0 || index >= _matches.Count) return;

            SelectedLocation = _matches[index];
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
                _cancellation = null;
            }
            base.Dispose(disposing);
        }
    }
}
