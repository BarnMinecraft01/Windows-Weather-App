using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Json;
using WeatherApp.Models;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>What one Open-Meteo forecast request produced.</summary>
    public sealed class OpenMeteoResult
    {
        public OpenMeteoResult()
        {
            Days = new List<ForecastDay>();
            Hours = new List<HourlyPoint>();
        }

        public CurrentConditions Current { get; set; }
        public List<ForecastDay> Days { get; private set; }
        public List<HourlyPoint> Hours { get; private set; }

        /// <summary>Seconds east of UTC at the forecast location.</summary>
        public int UtcOffsetSeconds { get; set; }

        public string TimeZone { get; set; }
    }

    /// <summary>
    /// Client for Open-Meteo, which fills three gaps the NWS API leaves.
    ///
    ///  1. The NWS forecast stops at seven days; the user asked for ten.
    ///  2. Open-Meteo separates rain from snow from showers hour by hour, and
    ///     reports CAPE, which is what makes a useful thunderstorm signal.
    ///  3. It serves pressure-level winds, so the jet stream map can be drawn from
    ///     real 250 hPa data rather than screen-scraped from a chart image.
    ///
    /// It needs no API key, which matters for an app meant to be installed and
    /// forgotten about.
    /// </summary>
    public sealed class OpenMeteoClient
    {
        private static readonly TimeSpan ForecastTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan UpperAirTtl = TimeSpan.FromMinutes(60);

        private const string DailyVariables =
            "temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min," +
            "precipitation_sum,rain_sum,showers_sum,snowfall_sum,precipitation_probability_max," +
            "precipitation_hours,weather_code,wind_speed_10m_max,wind_gusts_10m_max," +
            "wind_direction_10m_dominant,uv_index_max,sunrise,sunset";

        private const string HourlyVariables =
            "temperature_2m,apparent_temperature,relative_humidity_2m,dew_point_2m," +
            "precipitation_probability,precipitation,rain,showers,snowfall,weather_code," +
            "cloud_cover,visibility,wind_speed_10m,wind_direction_10m,wind_gusts_10m," +
            "pressure_msl,cape";

        private const string CurrentVariables =
            "temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation," +
            "rain,showers,snowfall,weather_code,cloud_cover,pressure_msl,surface_pressure," +
            "wind_speed_10m,wind_direction_10m,wind_gusts_10m";

        /// <summary>
        /// Fetches current conditions plus a daily and hourly forecast.
        /// </summary>
        /// <param name="forecastDays">Days to request; the API allows up to 16.</param>
        public async Task<OpenMeteoResult> GetForecastAsync(
            GeoLocation location, int forecastDays, CancellationToken cancellationToken)
        {
            if (forecastDays < 1) forecastDays = 1;
            if (forecastDays > 16) forecastDays = 16;

            var url = new StringBuilder(Endpoints.OpenMeteoForecast);
            url.Append("?latitude=").Append(Fixed(location.Latitude));
            url.Append("&longitude=").Append(Fixed(location.Longitude));
            url.Append("&timezone=auto");
            url.Append("&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch");
            url.Append("&forecast_days=").Append(forecastDays.ToString(CultureInfo.InvariantCulture));
            url.Append("&daily=").Append(DailyVariables);
            url.Append("&hourly=").Append(HourlyVariables);
            url.Append("&current=").Append(CurrentVariables);

            JsonValue root = await HttpService
                .GetJsonAsync(url.ToString(), "application/json", ForecastTtl, cancellationToken)
                .ConfigureAwait(false);

            var result = new OpenMeteoResult
            {
                UtcOffsetSeconds = root["utc_offset_seconds"].AsInt(0),
                TimeZone = root["timezone"].AsString(null)
            };

            ReadCurrent(root["current"], result);
            ReadDaily(root["daily"], result);
            ReadHourly(root["hourly"], result);
            return result;
        }

        private static void ReadCurrent(JsonValue current, OpenMeteoResult result)
        {
            if (!current.Exists) return;

            DateTime? time = current["time"].AsDateTime();
            int code = current["weather_code"].AsInt(-1);

            result.Current = new CurrentConditions
            {
                ObservedAt = time ?? DateTime.Now,
                TemperatureF = current["temperature_2m"].AsNullableDouble(),
                FeelsLikeF = current["apparent_temperature"].AsNullableDouble(),
                RelativeHumidity = current["relative_humidity_2m"].AsNullableDouble(),
                WindSpeedMph = current["wind_speed_10m"].AsNullableDouble(),
                WindGustMph = current["wind_gusts_10m"].AsNullableDouble(),
                WindDirectionDegrees = current["wind_direction_10m"].AsNullableDouble(),
                PressureMb = current["pressure_msl"].AsNullableDouble(),
                CloudCoverPercent = current["cloud_cover"].AsNullableDouble(),
                PrecipitationRateInches = current["precipitation"].AsNullableDouble(),
                WeatherCode = code < 0 ? 0 : code,
                IsDaytime = current["is_day"].AsInt(1) == 1,
                Summary = code < 0 ? null : WeatherCodes.Describe(code),
                Source = "Open-Meteo"
            };
        }

        private static void ReadDaily(JsonValue daily, OpenMeteoResult result)
        {
            if (!daily.Exists) return;

            JsonValue times = daily["time"];
            for (int i = 0; i < times.Count; i++)
            {
                DateTime? date = times[i].AsDateTime();
                if (!date.HasValue) continue;

                int code = daily["weather_code"][i].AsInt(0);

                result.Days.Add(new ForecastDay
                {
                    Date = date.Value.Date,
                    HighF = daily["temperature_2m_max"][i].AsNullableDouble(),
                    LowF = daily["temperature_2m_min"][i].AsNullableDouble(),
                    PrecipitationProbability = daily["precipitation_probability_max"][i].AsNullableDouble(),
                    PrecipitationInches = daily["precipitation_sum"][i].AsNullableDouble(),
                    RainInches = daily["rain_sum"][i].AsNullableDouble(),
                    SnowfallInches = daily["snowfall_sum"][i].AsNullableDouble(),
                    WindSpeedMph = daily["wind_speed_10m_max"][i].AsNullableDouble(),
                    WindGustMph = daily["wind_gusts_10m_max"][i].AsNullableDouble(),
                    WindDirectionDegrees = daily["wind_direction_10m_dominant"][i].AsNullableDouble(),
                    UvIndexMax = daily["uv_index_max"][i].AsNullableDouble(),
                    Sunrise = daily["sunrise"][i].AsDateTime(),
                    Sunset = daily["sunset"][i].AsDateTime(),
                    WeatherCode = code,
                    Summary = WeatherCodes.Describe(code),
                    Source = "Open-Meteo"
                });
            }
        }

        private static void ReadHourly(JsonValue hourly, OpenMeteoResult result)
        {
            if (!hourly.Exists) return;

            JsonValue times = hourly["time"];
            for (int i = 0; i < times.Count; i++)
            {
                DateTime? time = times[i].AsDateTime();
                if (!time.HasValue) continue;

                int code = hourly["weather_code"][i].AsInt(0);
                double? visibilityFeet = hourly["visibility"][i].AsNullableDouble();

                result.Hours.Add(new HourlyPoint
                {
                    Time = time.Value,
                    TemperatureF = hourly["temperature_2m"][i].AsNullableDouble(),
                    FeelsLikeF = hourly["apparent_temperature"][i].AsNullableDouble(),
                    RelativeHumidity = hourly["relative_humidity_2m"][i].AsNullableDouble(),
                    PrecipitationProbability = hourly["precipitation_probability"][i].AsNullableDouble(),
                    PrecipitationInches = hourly["precipitation"][i].AsNullableDouble(),
                    RainInches = hourly["rain"][i].AsNullableDouble(),
                    SnowfallInches = hourly["snowfall"][i].AsNullableDouble(),
                    WindSpeedMph = hourly["wind_speed_10m"][i].AsNullableDouble(),
                    WindGustMph = hourly["wind_gusts_10m"][i].AsNullableDouble(),
                    WindDirectionDegrees = hourly["wind_direction_10m"][i].AsNullableDouble(),
                    CloudCoverPercent = hourly["cloud_cover"][i].AsNullableDouble(),
                    CapeJoules = hourly["cape"][i].AsNullableDouble(),
                    WeatherCode = code,
                    Summary = WeatherCodes.Describe(code)
                });

                // Open-Meteo reports visibility in feet when imperial units are asked
                // for; the current-conditions panel wants miles.
                if (visibilityFeet.HasValue && result.Current != null
                    && !result.Current.VisibilityMiles.HasValue
                    && i == 0)
                {
                    result.Current.VisibilityMiles = visibilityFeet.Value / 5280d;
                }
            }
        }

        // ---- Upper air / jet stream -------------------------------------------

        /// <summary>
        /// Samples winds at a constant pressure level across a lat/lon box.
        ///
        /// Open-Meteo accepts several coordinates in one request, so a grid costs a
        /// handful of calls rather than one per point. Requests are chunked and run
        /// in sequence to stay well inside the free service's fair-use limits, and
        /// the result is cached for an hour because the underlying model only runs
        /// every six.
        /// </summary>
        /// <param name="hoursAhead">0 for the current analysis, or a forecast hour.</param>
        public async Task<JetStreamField> GetJetStreamAsync(
            Region region, int pressureLevelHpa, int hoursAhead, CancellationToken cancellationToken)
        {
            if (pressureLevelHpa != 250 && pressureLevelHpa != 300) pressureLevelHpa = 250;
            if (hoursAhead < 0) hoursAhead = 0;

            var field = new JetStreamField
            {
                SouthLatitude = region.SouthLatitude,
                NorthLatitude = region.NorthLatitude,
                WestLongitude = region.WestLongitude,
                EastLongitude = region.EastLongitude,
                Columns = Math.Max(2, region.GridColumns),
                Rows = Math.Max(2, region.GridRows),
                ModelName = "GFS " + pressureLevelHpa.ToString(CultureInfo.InvariantCulture) + " hPa"
            };

            // Row-major from the north-west corner, matching JetStreamField.At.
            var coordinates = new List<double[]>();
            for (int row = 0; row < field.Rows; row++)
            {
                double latitude = field.NorthLatitude
                                  - (field.NorthLatitude - field.SouthLatitude) * row / (field.Rows - 1);

                for (int column = 0; column < field.Columns; column++)
                {
                    double longitude = field.WestLongitude
                                       + (field.EastLongitude - field.WestLongitude) * column / (field.Columns - 1);
                    coordinates.Add(new[] { latitude, longitude });
                }
            }

            string level = pressureLevelHpa.ToString(CultureInfo.InvariantCulture) + "hPa";
            string variables = "wind_speed_" + level + ",wind_direction_" + level
                               + ",geopotential_height_" + level + ",temperature_" + level;

            // Enough days to cover the longest offset the UI offers.
            int forecastDays = Math.Max(1, (int)Math.Ceiling((hoursAhead + 1) / 24d));

            const int ChunkSize = 25;
            for (int offset = 0; offset < coordinates.Count; offset += ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int take = Math.Min(ChunkSize, coordinates.Count - offset);
                List<double[]> chunk = coordinates.GetRange(offset, take);

                JsonValue response = await FetchUpperAirChunkAsync(
                    chunk, variables, forecastDays, cancellationToken).ConfigureAwait(false);

                // A multi-coordinate request answers with an array; a single
                // coordinate answers with a bare object.
                if (response.IsArray)
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        AppendPoint(field, response[i], chunk[i], level, hoursAhead);
                    }
                }
                else
                {
                    AppendPoint(field, response, chunk[0], level, hoursAhead);
                }
            }

            Summarise(field);
            return field;
        }

        private static async Task<JsonValue> FetchUpperAirChunkAsync(
            List<double[]> chunk, string variables, int forecastDays, CancellationToken cancellationToken)
        {
            var latitudes = new StringBuilder();
            var longitudes = new StringBuilder();

            for (int i = 0; i < chunk.Count; i++)
            {
                if (i > 0) { latitudes.Append(','); longitudes.Append(','); }
                latitudes.Append(Fixed(chunk[i][0]));
                longitudes.Append(Fixed(chunk[i][1]));
            }

            var url = new StringBuilder(Endpoints.OpenMeteoForecast);
            url.Append("?latitude=").Append(latitudes);
            url.Append("&longitude=").Append(longitudes);
            url.Append("&hourly=").Append(variables);
            url.Append("&wind_speed_unit=kn");          // upper-air charts are drawn in knots
            url.Append("&temperature_unit=celsius");
            url.Append("&timezone=UTC");                 // one clock for the whole grid
            url.Append("&forecast_days=").Append(forecastDays.ToString(CultureInfo.InvariantCulture));

            return await HttpService
                .GetJsonAsync(url.ToString(), "application/json", UpperAirTtl, cancellationToken)
                .ConfigureAwait(false);
        }

        private static void AppendPoint(
            JetStreamField field, JsonValue node, double[] coordinate, string level, int hoursAhead)
        {
            JsonValue hourly = node["hourly"];

            // Index by the run's own time axis rather than assuming hour zero is now:
            // Open-Meteo starts the series at midnight UTC of the current day.
            int index = FindHourIndex(hourly["time"], hoursAhead);

            var point = new JetStreamPoint
            {
                Latitude = coordinate[0],
                Longitude = coordinate[1],
                SpeedKnots = hourly["wind_speed_" + level][index].AsDouble(0d),
                DirectionDegrees = hourly["wind_direction_" + level][index].AsDouble(0d),
                GeopotentialHeightM = hourly["geopotential_height_" + level][index].AsNullableDouble(),
                TemperatureC = hourly["temperature_" + level][index].AsNullableDouble()
            };

            field.Points.Add(point);

            if (field.ValidTime == default(DateTime))
            {
                DateTime? valid = hourly["time"][index].AsDateTime();
                if (valid.HasValue) field.ValidTime = valid.Value;
            }
        }

        /// <summary>
        /// Locates the sample closest to "now plus hoursAhead" on a UTC time axis.
        /// </summary>
        private static int FindHourIndex(JsonValue times, int hoursAhead)
        {
            if (times.Count == 0) return 0;

            DateTime target = DateTime.UtcNow.AddHours(hoursAhead);
            int bestIndex = 0;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < times.Count; i++)
            {
                DateTime? candidate = times[i].AsDateTime();
                if (!candidate.HasValue) continue;

                double distance = Math.Abs((candidate.Value - target).TotalMinutes);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static void Summarise(JetStreamField field)
        {
            foreach (JetStreamPoint point in field.Points)
            {
                if (point.SpeedKnots > field.MaxSpeedKnots)
                {
                    field.MaxSpeedKnots = point.SpeedKnots;
                    field.StrongestPoint = point;
                }
            }
        }

        private static string Fixed(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
