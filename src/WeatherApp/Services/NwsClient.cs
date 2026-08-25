using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Json;
using WeatherApp.Models;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>The quantitative layers of the NWS raw forecast grid.</summary>
    public sealed class GridpointData
    {
        public GridSeries ProbabilityOfPrecipitation { get; set; }
        public GridSeries ProbabilityOfThunder { get; set; }
        public GridSeries SnowfallAmount { get; set; }
        public GridSeries IceAccumulation { get; set; }
        public GridSeries QuantitativePrecipitation { get; set; }
        public GridSeries Temperature { get; set; }
        public GridSeries ApparentTemperature { get; set; }
        public GridSeries SkyCover { get; set; }
        public GridSeries WindSpeed { get; set; }
        public GridSeries WindGust { get; set; }

        /// <summary>Text hazard layer, e.g. "Chance of thunderstorms". Not always issued.</summary>
        public string ForecastOffice { get; set; }
    }

    /// <summary>
    /// Client for api.weather.gov -- the official US source, and the only one that
    /// issues watches, warnings and advisories.
    ///
    /// The NWS forecast reaches seven days; the extended days beyond that come from
    /// <see cref="OpenMeteoClient"/>. Where the two overlap, NWS wins, because it is
    /// the human-reviewed product the alerts are written against.
    /// </summary>
    public sealed class NwsClient
    {
        private static readonly TimeSpan PointTtl = TimeSpan.FromDays(7);
        private static readonly TimeSpan ForecastTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan GridTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan AlertTtl = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Resolves a coordinate to its forecast office and grid cell, and fills in
        /// the radar station and zone. The result rarely changes, so it is cached for
        /// a week -- the NWS documentation explicitly asks callers to do this.
        /// </summary>
        public async Task<bool> ResolvePointAsync(GeoLocation location, CancellationToken cancellationToken)
        {
            if (location == null) return false;

            string url = Endpoints.NwsApiBase + "/points/" + location.ApiCoordinates;
            JsonValue root = await HttpService
                .GetJsonAsync(url, Endpoints.NwsAccept, PointTtl, cancellationToken)
                .ConfigureAwait(false);

            JsonValue properties = root["properties"];
            if (!properties.Exists) return false;

            string office = properties["gridId"].AsString(properties["cwa"].AsString(null));
            if (string.IsNullOrEmpty(office)) return false;

            location.ForecastOffice = office;
            location.GridX = properties["gridX"].AsInt(-1);
            location.GridY = properties["gridY"].AsInt(-1);
            location.RadarStation = properties["radarStation"].AsString(null);
            location.TimeZone = properties["timeZone"].AsString(location.TimeZone);
            location.ZoneId = LastSegment(properties["forecastZone"].AsString(null));

            // The point response knows the nearest named place, which beats showing
            // bare coordinates for somewhere the user clicked on a map.
            if (string.IsNullOrWhiteSpace(location.Name))
            {
                JsonValue relative = properties["relativeLocation"]["properties"];
                location.Name = relative["city"].AsString(location.Name);
                location.Admin = relative["state"].AsString(location.Admin);
            }

            return location.GridX >= 0 && location.GridY >= 0;
        }

        /// <summary>
        /// The seven-day forecast, folded from the API's alternating day/night
        /// periods into one entry per calendar day.
        /// </summary>
        public async Task<List<ForecastDay>> GetForecastAsync(
            GeoLocation location, CancellationToken cancellationToken)
        {
            var days = new List<ForecastDay>();
            if (!HasGrid(location)) return days;

            string url = GridUrl(location) + "/forecast";
            JsonValue root = await HttpService
                .GetJsonAsync(url, Endpoints.NwsAccept, ForecastTtl, cancellationToken)
                .ConfigureAwait(false);

            var byDate = new Dictionary<DateTime, ForecastDay>();
            var order = new List<DateTime>();

            foreach (JsonValue period in root["properties"]["periods"].Items)
            {
                DateTime? start = period["startTime"].AsDateTime();
                if (!start.HasValue) continue;

                bool isDaytime = period["isDaytime"].AsBool(true);

                // An overnight period that begins in the evening belongs to the day it
                // started on; one that begins after midnight belongs to the day before,
                // so that "Monday" carries Monday's high and Monday night's low.
                DateTime date = start.Value.Date;
                if (!isDaytime && start.Value.Hour < 12) date = date.AddDays(-1);

                ForecastDay day;
                if (!byDate.TryGetValue(date, out day))
                {
                    day = new ForecastDay { Date = date, Source = "NWS" };
                    byDate[date] = day;
                    order.Add(date);
                }

                double? temperature = period["temperature"].AsNullableDouble();
                if (temperature.HasValue
                    && !string.Equals(period["temperatureUnit"].AsString("F"), "F", StringComparison.OrdinalIgnoreCase))
                {
                    temperature = Units.CelsiusToFahrenheit(temperature.Value);
                }

                if (isDaytime) day.HighF = temperature;
                else day.LowF = temperature;

                double? pop = period["probabilityOfPrecipitation"]["value"].AsNullableDouble();
                if (pop.HasValue && (!day.PrecipitationProbability.HasValue
                                     || pop.Value > day.PrecipitationProbability.Value))
                {
                    day.PrecipitationProbability = pop;
                }

                double? wind = ParseWindSpeed(period["windSpeed"].AsString(null));
                if (wind.HasValue && (!day.WindSpeedMph.HasValue || wind.Value > day.WindSpeedMph.Value))
                {
                    day.WindSpeedMph = wind;
                    day.WindDirectionDegrees = CardinalToDegrees(period["windDirection"].AsString(null));
                }

                // Prefer the daytime narrative; it is the one people read.
                if (isDaytime || string.IsNullOrEmpty(day.Summary))
                {
                    day.Summary = period["shortForecast"].AsString(day.Summary);
                    day.DetailedForecast = period["detailedForecast"].AsString(day.DetailedForecast);
                }
            }

            foreach (DateTime date in order)
            {
                days.Add(byDate[date]);
            }
            days.Sort((a, b) => a.Date.CompareTo(b.Date));
            return days;
        }

        public async Task<List<HourlyPoint>> GetHourlyAsync(
            GeoLocation location, CancellationToken cancellationToken)
        {
            var hours = new List<HourlyPoint>();
            if (!HasGrid(location)) return hours;

            string url = GridUrl(location) + "/forecast/hourly";
            JsonValue root = await HttpService
                .GetJsonAsync(url, Endpoints.NwsAccept, ForecastTtl, cancellationToken)
                .ConfigureAwait(false);

            foreach (JsonValue period in root["properties"]["periods"].Items)
            {
                DateTime? start = period["startTime"].AsDateTime();
                if (!start.HasValue) continue;

                double? temperature = period["temperature"].AsNullableDouble();
                if (temperature.HasValue
                    && !string.Equals(period["temperatureUnit"].AsString("F"), "F", StringComparison.OrdinalIgnoreCase))
                {
                    temperature = Units.CelsiusToFahrenheit(temperature.Value);
                }

                double? humidity = period["relativeHumidity"]["value"].AsNullableDouble();

                hours.Add(new HourlyPoint
                {
                    Time = start.Value,
                    TemperatureF = temperature,
                    PrecipitationProbability = period["probabilityOfPrecipitation"]["value"].AsNullableDouble(),
                    RelativeHumidity = humidity,
                    WindSpeedMph = ParseWindSpeed(period["windSpeed"].AsString(null)),
                    WindDirectionDegrees = CardinalToDegrees(period["windDirection"].AsString(null)),
                    Summary = period["shortForecast"].AsString(null)
                });
            }

            return hours;
        }

        /// <summary>
        /// The raw forecast grid. This is where the separate snow, ice and thunder
        /// series live; the prose forecast does not break them out.
        /// </summary>
        public async Task<GridpointData> GetGridDataAsync(
            GeoLocation location, CancellationToken cancellationToken)
        {
            if (!HasGrid(location)) return null;

            JsonValue root = await HttpService
                .GetJsonAsync(GridUrl(location), Endpoints.NwsAccept, GridTtl, cancellationToken)
                .ConfigureAwait(false);

            JsonValue p = root["properties"];
            if (!p.Exists) return null;

            return new GridpointData
            {
                ForecastOffice = location.ForecastOffice,
                ProbabilityOfPrecipitation = GridSeries.Parse(p["probabilityOfPrecipitation"]),
                ProbabilityOfThunder = GridSeries.Parse(p["probabilityOfThunder"]),
                SnowfallAmount = GridSeries.Parse(p["snowfallAmount"]),
                IceAccumulation = GridSeries.Parse(p["iceAccumulation"]),
                QuantitativePrecipitation = GridSeries.Parse(p["quantitativePrecipitation"]),
                Temperature = GridSeries.Parse(p["temperature"]),
                ApparentTemperature = GridSeries.Parse(p["apparentTemperature"]),
                SkyCover = GridSeries.Parse(p["skyCover"]),
                WindSpeed = GridSeries.Parse(p["windSpeed"]),
                WindGust = GridSeries.Parse(p["windGust"])
            };
        }

        /// <summary>Active alerts for a single point -- the tightest possible scope.</summary>
        public Task<AlertCollection> GetAlertsForPointAsync(
            GeoLocation location, CancellationToken cancellationToken)
        {
            string url = Endpoints.NwsApiBase + "/alerts/active?point=" + location.ApiCoordinates;
            return GetAlertsAsync(url, location.DisplayName, cancellationToken);
        }

        /// <summary>Active alerts across a whole region, by state / marine area code.</summary>
        public Task<AlertCollection> GetAlertsForAreasAsync(
            IEnumerable<string> areas, string scopeDescription, CancellationToken cancellationToken)
        {
            string joined = string.Join(",", areas.ToArray());
            string url = Endpoints.NwsApiBase + "/alerts/active?area=" + Uri.EscapeDataString(joined);
            return GetAlertsAsync(url, scopeDescription, cancellationToken);
        }

        private async Task<AlertCollection> GetAlertsAsync(
            string url, string scopeDescription, CancellationToken cancellationToken)
        {
            var collection = new AlertCollection
            {
                ScopeDescription = scopeDescription,
                RetrievedAt = DateTime.Now
            };

            JsonValue root = await HttpService
                .GetJsonAsync(url, Endpoints.NwsAccept, AlertTtl, cancellationToken)
                .ConfigureAwait(false);

            foreach (JsonValue feature in root["features"].Items)
            {
                JsonValue p = feature["properties"];
                if (!p.Exists) continue;

                collection.Alerts.Add(new WeatherAlert
                {
                    Id = p["id"].AsString(null),
                    Event = p["event"].AsString("Weather Alert"),
                    Headline = p["headline"].AsString(null),
                    Description = p["description"].AsString(null),
                    Instruction = p["instruction"].AsString(null),
                    AreaDescription = p["areaDesc"].AsString(null),
                    Severity = p["severity"].AsString(null),
                    Certainty = p["certainty"].AsString(null),
                    Urgency = p["urgency"].AsString(null),
                    SenderName = p["senderName"].AsString(null),
                    Onset = p["onset"].AsDateTime(),
                    Effective = p["effective"].AsDateTime(),
                    Expires = p["expires"].AsDateTime()
                });
            }

            collection.Alerts.Sort((a, b) =>
            {
                int byPriority = a.Priority.CompareTo(b.Priority);
                if (byPriority != 0) return byPriority;

                // Within a priority band, soonest to expire first.
                DateTime aExpires = a.Expires ?? DateTime.MaxValue;
                DateTime bExpires = b.Expires ?? DateTime.MaxValue;
                return aExpires.CompareTo(bExpires);
            });

            return collection;
        }

        // ---- helpers ---------------------------------------------------------

        private static bool HasGrid(GeoLocation location)
        {
            return location != null
                   && !string.IsNullOrEmpty(location.ForecastOffice)
                   && location.GridX >= 0
                   && location.GridY >= 0;
        }

        private static string GridUrl(GeoLocation location)
        {
            return Endpoints.NwsApiBase + "/gridpoints/" + location.ForecastOffice + "/"
                   + location.GridX.ToString(CultureInfo.InvariantCulture) + ","
                   + location.GridY.ToString(CultureInfo.InvariantCulture);
        }

        private static string LastSegment(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int slash = url.LastIndexOf('/');
            return slash >= 0 && slash + 1 < url.Length ? url.Substring(slash + 1) : url;
        }

        /// <summary>
        /// Reads the API's wind strings, which come as "10 mph" or "10 to 15 mph".
        /// The upper bound is the useful number, so a range yields its maximum.
        /// </summary>
        internal static double? ParseWindSpeed(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            double? best = null;
            var digits = new StringBuilder();

            foreach (char c in text)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                    continue;
                }

                if (digits.Length > 0)
                {
                    double parsed;
                    if (double.TryParse(digits.ToString(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsed))
                    {
                        if (!best.HasValue || parsed > best.Value) best = parsed;
                    }
                    digits.Clear();
                }
            }

            if (digits.Length > 0)
            {
                double parsed;
                if (double.TryParse(digits.ToString(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out parsed))
                {
                    if (!best.HasValue || parsed > best.Value) best = parsed;
                }
            }

            // NWS reports wind in km/h for a handful of offices outside the lower 48.
            if (best.HasValue && text.IndexOf("km", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                best = Units.KilometresPerHourToMph(best.Value);
            }
            return best;
        }

        /// <summary>Turns a compass abbreviation such as "WNW" back into degrees.</summary>
        internal static double? CardinalToDegrees(string cardinal)
        {
            if (string.IsNullOrWhiteSpace(cardinal)) return null;

            string key = cardinal.Trim().ToUpperInvariant();
            string[] order =
            {
                "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
            };

            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == key) return i * 22.5d;
            }
            return null;
        }
    }
}
