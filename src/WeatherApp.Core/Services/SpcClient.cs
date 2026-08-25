using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Json;
using WeatherApp.Models;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>SPC severe-weather probabilities for one point on one outlook day.</summary>
    public sealed class SevereOutlook
    {
        public int Day { get; set; }

        /// <summary>Probability of severe hail within 25 miles, 0-100.</summary>
        public double? HailProbability { get; set; }

        public double? WindProbability { get; set; }
        public double? TornadoProbability { get; set; }

        /// <summary>Categorical risk: Marginal, Slight, Enhanced, Moderate or High.</summary>
        public string Category { get; set; }
    }

    /// <summary>
    /// Reads the Storm Prediction Center convective outlook.
    ///
    /// This exists because the user asked for hail chances, and no general forecast
    /// API publishes them. The SPC does, as probability-of-severe polygons, served
    /// through an ArcGIS layer that can be queried point-in-polygon.
    ///
    /// Two deliberate constraints:
    ///  * The outlook only runs three days out. Beyond Day 3 the app reports no
    ///    hail number at all rather than inventing one.
    ///  * The layer ids are discovered by name at runtime instead of hard-coded,
    ///    because they have been renumbered before. A failure here is downgraded to
    ///    a note on the snapshot -- a missing hail row must not cost the user their
    ///    forecast.
    /// </summary>
    public sealed class SpcClient
    {
        private static readonly TimeSpan LayerTtl = TimeSpan.FromHours(24);
        private static readonly TimeSpan OutlookTtl = TimeSpan.FromMinutes(20);

        private Dictionary<string, int> _layerCache;

        /// <summary>
        /// Fetches Day 1 through Day 3 outlooks for a point. Days with no coverage
        /// come back with null probabilities, which the UI renders as a dash.
        /// </summary>
        public async Task<List<SevereOutlook>> GetOutlookAsync(
            GeoLocation location, CancellationToken cancellationToken)
        {
            var outlooks = new List<SevereOutlook>();

            Dictionary<string, int> layers = await GetLayerIdsAsync(cancellationToken).ConfigureAwait(false);
            if (layers.Count == 0) return outlooks;

            for (int day = 1; day <= 3; day++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var outlook = new SevereOutlook { Day = day };

                outlook.HailProbability = await ProbabilityAsync(
                    layers, day, "hail", location, cancellationToken).ConfigureAwait(false);
                outlook.WindProbability = await ProbabilityAsync(
                    layers, day, "wind", location, cancellationToken).ConfigureAwait(false);
                outlook.TornadoProbability = await ProbabilityAsync(
                    layers, day, "tornado", location, cancellationToken).ConfigureAwait(false);
                outlook.Category = await CategoryAsync(
                    layers, day, location, cancellationToken).ConfigureAwait(false);

                outlooks.Add(outlook);
            }

            return outlooks;
        }

        /// <summary>
        /// Builds a name-to-id map of the outlook layers.
        ///
        /// Keys are normalised to "day{n}-{peril}", e.g. "day1-hail", so the caller
        /// never has to know the service's own wording.
        /// </summary>
        private async Task<Dictionary<string, int>> GetLayerIdsAsync(CancellationToken cancellationToken)
        {
            if (_layerCache != null) return _layerCache;

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string url = Endpoints.SpcOutlookMapServer + "/layers?f=json";
                JsonValue root = await HttpService
                    .GetJsonAsync(url, "application/json", LayerTtl, cancellationToken)
                    .ConfigureAwait(false);

                foreach (JsonValue layer in root["layers"].Items)
                {
                    string name = layer["name"].AsString(string.Empty);
                    int id = layer["id"].AsInt(-1);
                    if (id < 0 || string.IsNullOrEmpty(name)) continue;

                    string key = ClassifyLayer(name);
                    if (key != null && !map.ContainsKey(key)) map[key] = id;
                }
            }
            catch (WeatherServiceException)
            {
                // Leave the map empty; the caller treats that as "no outlook available".
            }

            _layerCache = map;
            return map;
        }

        /// <summary>
        /// Maps a service layer name such as "Day 1 Probabilistic Hail" onto the
        /// app's own key. Returns null for layers the app does not use, including
        /// the "significant" 10%-hatched sub-layers, which would otherwise shadow
        /// the plain probability layer.
        /// </summary>
        internal static string ClassifyLayer(string name)
        {
            string lower = name.ToLowerInvariant();

            int day;
            if (lower.Contains("day 1") || lower.Contains("day1")) day = 1;
            else if (lower.Contains("day 2") || lower.Contains("day2")) day = 2;
            else if (lower.Contains("day 3") || lower.Contains("day3")) day = 3;
            else return null;

            // "Significant severe" layers are a hatched overlay on top of the
            // probability polygons, not a probability in their own right.
            if (lower.Contains("significant") || lower.Contains("sig ")) return null;

            string prefix = "day" + day.ToString(CultureInfo.InvariantCulture) + "-";

            if (lower.Contains("hail")) return prefix + "hail";
            if (lower.Contains("wind")) return prefix + "wind";
            if (lower.Contains("tornado")) return prefix + "tornado";
            if (lower.Contains("categorical") || lower.Contains("cat")) return prefix + "categorical";

            // Days 2 and 3 issue a single combined "probabilistic" severe layer
            // instead of splitting the perils out.
            if (lower.Contains("probabilistic") || lower.Contains("prob")) return prefix + "severe";

            return null;
        }

        private async Task<double?> ProbabilityAsync(
            Dictionary<string, int> layers, int day, string peril,
            GeoLocation location, CancellationToken cancellationToken)
        {
            string dayPrefix = "day" + day.ToString(CultureInfo.InvariantCulture) + "-";

            int layerId;
            if (!layers.TryGetValue(dayPrefix + peril, out layerId))
            {
                // Days 2-3 combine the perils; fall back to the combined layer so the
                // user still sees a severe-storm number rather than a blank.
                if (!layers.TryGetValue(dayPrefix + "severe", out layerId)) return null;
            }

            JsonValue attributes = await QueryPointAsync(layerId, location, cancellationToken)
                .ConfigureAwait(false);

            return attributes == null ? null : ExtractProbability(attributes);
        }

        private async Task<string> CategoryAsync(
            Dictionary<string, int> layers, int day,
            GeoLocation location, CancellationToken cancellationToken)
        {
            int layerId;
            string key = "day" + day.ToString(CultureInfo.InvariantCulture) + "-categorical";
            if (!layers.TryGetValue(key, out layerId)) return null;

            JsonValue attributes = await QueryPointAsync(layerId, location, cancellationToken)
                .ConfigureAwait(false);

            return attributes == null ? null : ExtractCategory(attributes);
        }

        /// <summary>Returns the attributes of the outlook polygon containing the point.</summary>
        private static async Task<JsonValue> QueryPointAsync(
            int layerId, GeoLocation location, CancellationToken cancellationToken)
        {
            string url = Endpoints.SpcOutlookMapServer + "/"
                + layerId.ToString(CultureInfo.InvariantCulture) + "/query"
                + "?geometry=" + Fixed(location.Longitude) + "," + Fixed(location.Latitude)
                + "&geometryType=esriGeometryPoint"
                + "&inSR=4326"
                + "&spatialRel=esriSpatialRelIntersects"
                + "&outFields=*"
                + "&returnGeometry=false"
                + "&f=json";

            try
            {
                JsonValue root = await HttpService
                    .GetJsonAsync(url, "application/json", OutlookTtl, cancellationToken)
                    .ConfigureAwait(false);

                JsonValue features = root["features"];
                if (features.Count == 0) return null;   // outside every risk polygon

                // Overlapping polygons are ordered lowest risk first; the last one is
                // the highest probability covering this point.
                return features[features.Count - 1]["attributes"];
            }
            catch (WeatherServiceException)
            {
                return null;
            }
        }

        /// <summary>
        /// Pulls a percentage out of an SPC feature's attributes.
        ///
        /// The service has used several field names over the years ("dn", "LABEL",
        /// "idp"), and the label is sometimes "0.15" and sometimes "15%", so every
        /// plausible field is tried and the value is normalised to 0-100.
        /// </summary>
        internal static double? ExtractProbability(JsonValue attributes)
        {
            string[] candidates = { "LABEL", "label", "dn", "DN", "idp", "IDP", "PROB", "prob", "Prob" };

            foreach (string key in candidates)
            {
                JsonValue value = attributes[key];
                if (!value.Exists) continue;

                double? number = ParsePercentage(value.AsString(null));
                if (number.HasValue) return number;
            }
            return null;
        }

        internal static double? ParsePercentage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string trimmed = text.Trim().TrimEnd('%').Trim();

            // "SIGN" marks the significant-severe hatched area, not a probability.
            if (trimmed.Equals("SIGN", StringComparison.OrdinalIgnoreCase)) return null;

            double parsed;
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return null;
            }

            if (parsed < 0) return null;

            // Fractions such as 0.15 mean 15%; anything above 1 is already a percentage.
            if (parsed > 0 && parsed <= 1) parsed *= 100d;

            return parsed > 100d ? 100d : parsed;
        }

        /// <summary>Reads the categorical risk label, expanding SPC's abbreviations.</summary>
        internal static string ExtractCategory(JsonValue attributes)
        {
            string[] candidates = { "LABEL2", "label2", "LABEL", "label", "dn", "DN" };

            foreach (string key in candidates)
            {
                string value = attributes[key].AsString(null);
                if (string.IsNullOrWhiteSpace(value)) continue;

                string trimmed = value.Trim();
                switch (trimmed.ToUpperInvariant())
                {
                    case "TSTM": return "General Thunderstorms";
                    case "MRGL": return "Marginal";
                    case "SLGT": return "Slight";
                    case "ENH": return "Enhanced";
                    case "MDT": return "Moderate";
                    case "HIGH": return "High";
                }

                // Some layers already carry the spelled-out word.
                if (trimmed.Length > 2 && !char.IsDigit(trimmed[0])) return trimmed;
            }
            return null;
        }

        private static string Fixed(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
