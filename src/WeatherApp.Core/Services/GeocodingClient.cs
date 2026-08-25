using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Json;
using WeatherApp.Models;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>
    /// Turns what the user typed into a place.
    ///
    /// Accepts a city name, a US ZIP code, or a raw coordinate pair, because all
    /// three are things people reach for and none of them should need a different
    /// box to type into.
    /// </summary>
    public sealed class GeocodingClient
    {
        private static readonly TimeSpan SearchTtl = TimeSpan.FromHours(12);

        // "47.6062, -122.3321" and "47.6062 -122.3321" both parse.
        private static readonly Regex CoordinatePattern = new Regex(
            @"^\s*(-?\d{1,3}(?:\.\d+)?)\s*[, ]\s*(-?\d{1,3}(?:\.\d+)?)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex ZipPattern = new Regex(@"^\s*(\d{5})(?:-\d{4})?\s*$", RegexOptions.Compiled);

        public async Task<List<GeoLocation>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            var results = new List<GeoLocation>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            GeoLocation direct = TryParseCoordinates(query);
            if (direct != null)
            {
                results.Add(direct);
                return results;
            }

            string term = query.Trim();
            Match zip = ZipPattern.Match(term);
            if (zip.Success) term = zip.Groups[1].Value;

            string url = Endpoints.OpenMeteoGeocoding
                         + "?name=" + Uri.EscapeDataString(term)
                         + "&count=10&language=en&format=json";

            JsonValue root = await HttpService
                .GetJsonAsync(url, "application/json", SearchTtl, cancellationToken)
                .ConfigureAwait(false);

            foreach (JsonValue item in root["results"].Items)
            {
                double? latitude = item["latitude"].AsNullableDouble();
                double? longitude = item["longitude"].AsNullableDouble();
                if (!latitude.HasValue || !longitude.HasValue) continue;

                results.Add(new GeoLocation
                {
                    Name = item["name"].AsString(term),
                    Admin = item["admin1"].AsString(string.Empty),
                    Country = item["country"].AsString(string.Empty),
                    Latitude = latitude.Value,
                    Longitude = longitude.Value,
                    TimeZone = item["timezone"].AsString(null)
                });
            }

            // US places first: this app is built around US products, and a search for
            // "Portland" should not lead with Portland, Victoria.
            results.Sort((a, b) =>
            {
                int aUs = a.IsNwsCovered ? 0 : 1;
                int bUs = b.IsNwsCovered ? 0 : 1;
                return aUs.CompareTo(bUs);
            });

            return results;
        }

        /// <summary>Reads a "latitude, longitude" pair, or returns null if it is not one.</summary>
        internal static GeoLocation TryParseCoordinates(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            Match match = CoordinatePattern.Match(text);
            if (!match.Success) return null;

            double latitude, longitude;
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out latitude)) return null;
            if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out longitude)) return null;

            if (latitude < -90 || latitude > 90) return null;
            if (longitude < -180 || longitude > 180) return null;

            return new GeoLocation
            {
                Name = string.Empty,   // filled in by the NWS point lookup
                Admin = string.Empty,
                Country = "United States",
                Latitude = latitude,
                Longitude = longitude
            };
        }
    }
}
