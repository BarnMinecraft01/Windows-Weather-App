using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Models;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>
    /// Combines the three upstreams into a single snapshot.
    ///
    /// The merge rules, in short:
    ///  * Where the NWS has issued a forecast, it wins. It is human-reviewed and it
    ///    is what the watches and warnings are written against.
    ///  * Open-Meteo covers the days past the NWS seven-day window, supplies the
    ///    hour-by-hour split between rain and snow, and reports CAPE.
    ///  * The SPC convective outlook supplies hail and severe-storm probabilities
    ///    for Days 1-3; nothing publishes them further out.
    ///
    /// Every fetch is individually fault-tolerant. If the SPC layer is down the
    /// user still gets a forecast, with a note explaining what is missing, because
    /// a partial answer beats an error dialog when you are trying to find out
    /// whether to drive somewhere.
    /// </summary>
    public sealed class WeatherAggregator
    {
        private readonly NwsClient _nws = new NwsClient();
        private readonly OpenMeteoClient _openMeteo = new OpenMeteoClient();
        private readonly SpcClient _spc = new SpcClient();

        /// <summary>Days of forecast to build. The user asked for ten.</summary>
        public int ForecastDays { get; set; }

        public WeatherAggregator()
        {
            ForecastDays = 10;
        }

        public async Task<WeatherSnapshot> RefreshAsync(
            GeoLocation location, Region region, string alertScope, CancellationToken cancellationToken)
        {
            if (location == null) throw new ArgumentNullException("location");

            var snapshot = new WeatherSnapshot
            {
                Location = location,
                RetrievedAt = DateTime.Now
            };

            // Started first: neither depends on the NWS grid lookup.
            Task<OpenMeteoResult> openMeteoTask = Try(
                () => _openMeteo.GetForecastAsync(location, Math.Max(ForecastDays, 10), cancellationToken),
                snapshot.Warnings, "extended forecast");

            Task<List<SevereOutlook>> spcTask = Try(
                () => _spc.GetOutlookAsync(location, cancellationToken),
                snapshot.Warnings, "severe storm outlook");

            bool hasGrid = false;
            if (location.IsNwsCovered)
            {
                if (string.IsNullOrEmpty(location.ForecastOffice) || location.GridX < 0)
                {
                    try
                    {
                        hasGrid = await _nws.ResolvePointAsync(location, cancellationToken).ConfigureAwait(false);
                    }
                    catch (WeatherServiceException ex)
                    {
                        snapshot.Warnings.Add("National Weather Service grid lookup failed: " + ex.Message);
                    }
                }
                else
                {
                    hasGrid = true;
                }
            }
            else
            {
                snapshot.Warnings.Add(
                    "This location is outside National Weather Service coverage, so there are no official "
                    + "forecasts or alerts for it. Showing model data only.");
            }

            Task<List<ForecastDay>> nwsDaysTask = null;
            Task<List<HourlyPoint>> nwsHoursTask = null;
            Task<GridpointData> nwsGridTask = null;
            Task<AlertCollection> alertsTask = null;

            if (hasGrid)
            {
                snapshot.ForecastOfficeName = location.ForecastOffice;
                snapshot.RadarStation = location.RadarStation;

                nwsDaysTask = Try(() => _nws.GetForecastAsync(location, cancellationToken),
                    snapshot.Warnings, "NWS forecast");
                nwsHoursTask = Try(() => _nws.GetHourlyAsync(location, cancellationToken),
                    snapshot.Warnings, "NWS hourly forecast");
                nwsGridTask = Try(() => _nws.GetGridDataAsync(location, cancellationToken),
                    snapshot.Warnings, "NWS forecast grid");

                bool wholeRegion = string.Equals(alertScope, "region", StringComparison.OrdinalIgnoreCase);
                alertsTask = wholeRegion && region != null && region.AlertAreas.Count > 0
                    ? Try(() => _nws.GetAlertsForAreasAsync(region.AlertAreas, region.Title, cancellationToken),
                        snapshot.Warnings, "alerts")
                    : Try(() => _nws.GetAlertsForPointAsync(location, cancellationToken),
                        snapshot.Warnings, "alerts");
            }

            OpenMeteoResult model = await openMeteoTask.ConfigureAwait(false);
            List<ForecastDay> nwsDays = nwsDaysTask == null ? null : await nwsDaysTask.ConfigureAwait(false);
            List<HourlyPoint> nwsHours = nwsHoursTask == null ? null : await nwsHoursTask.ConfigureAwait(false);
            GridpointData grid = nwsGridTask == null ? null : await nwsGridTask.ConfigureAwait(false);
            AlertCollection alerts = alertsTask == null ? null : await alertsTask.ConfigureAwait(false);
            List<SevereOutlook> outlooks = await spcTask.ConfigureAwait(false);

            BuildCurrent(snapshot, model, nwsHours);
            BuildHours(snapshot, model, nwsHours);
            BuildDays(snapshot, model, nwsDays);
            BuildPrecipitation(snapshot, model, grid, outlooks);

            if (alerts != null)
            {
                snapshot.Alerts.ScopeDescription = alerts.ScopeDescription;
                snapshot.Alerts.RetrievedAt = alerts.RetrievedAt;
                snapshot.Alerts.Alerts.AddRange(alerts.Alerts);
            }

            if (!snapshot.HasAnyData)
            {
                throw new WeatherServiceException(
                    "No weather data could be retrieved for " + location.DisplayName
                    + ". Check the internet connection and try again.");
            }

            return snapshot;
        }

        // ---- merge steps -----------------------------------------------------

        private static void BuildCurrent(
            WeatherSnapshot snapshot, OpenMeteoResult model, List<HourlyPoint> nwsHours)
        {
            if (model == null || model.Current == null) return;

            snapshot.Current = model.Current;

            // The NWS short forecast for the current hour is better wording than a
            // WMO code lookup ("Patchy fog then sunny" versus "Mainly clear").
            if (nwsHours != null && nwsHours.Count > 0)
            {
                DateTime now = model.Current.ObservedAt;
                HourlyPoint match = nwsHours
                    .OrderBy(h => Math.Abs((h.Time - now).TotalMinutes))
                    .FirstOrDefault();

                if (match != null
                    && Math.Abs((match.Time - now).TotalHours) <= 2
                    && !string.IsNullOrWhiteSpace(match.Summary))
                {
                    snapshot.Current.Summary = match.Summary;
                    snapshot.Current.Source = "NWS / Open-Meteo";
                }
            }
        }

        private static void BuildHours(
            WeatherSnapshot snapshot, OpenMeteoResult model, List<HourlyPoint> nwsHours)
        {
            if (model == null) return;

            // Open-Meteo is the base because it separates rain, snow and CAPE, which
            // the NWS hourly product does not expose.
            snapshot.Hours.AddRange(model.Hours);

            if (nwsHours == null || nwsHours.Count == 0) return;

            var byHour = new Dictionary<DateTime, HourlyPoint>();
            foreach (HourlyPoint hour in nwsHours)
            {
                byHour[Floor(hour.Time)] = hour;
            }

            foreach (HourlyPoint hour in snapshot.Hours)
            {
                HourlyPoint official;
                if (!byHour.TryGetValue(Floor(hour.Time), out official)) continue;

                if (official.PrecipitationProbability.HasValue)
                {
                    hour.PrecipitationProbability = official.PrecipitationProbability;
                }
                if (!string.IsNullOrWhiteSpace(official.Summary)) hour.Summary = official.Summary;
                if (official.TemperatureF.HasValue) hour.TemperatureF = official.TemperatureF;
            }
        }

        private static void BuildDays(
            WeatherSnapshot snapshot, OpenMeteoResult model, List<ForecastDay> nwsDays)
        {
            var byDate = new Dictionary<DateTime, ForecastDay>();
            var order = new List<DateTime>();

            if (model != null)
            {
                foreach (ForecastDay day in model.Days)
                {
                    if (byDate.ContainsKey(day.Date)) continue;
                    byDate[day.Date] = day;
                    order.Add(day.Date);
                }
            }

            if (nwsDays != null)
            {
                foreach (ForecastDay official in nwsDays)
                {
                    ForecastDay day;
                    if (!byDate.TryGetValue(official.Date, out day))
                    {
                        byDate[official.Date] = official;
                        order.Add(official.Date);
                        continue;
                    }

                    // Overlay the official values, keeping the model's numbers for the
                    // fields the NWS product does not carry.
                    if (official.HighF.HasValue) day.HighF = official.HighF;
                    if (official.LowF.HasValue) day.LowF = official.LowF;
                    if (official.PrecipitationProbability.HasValue)
                    {
                        day.PrecipitationProbability = official.PrecipitationProbability;
                    }
                    if (official.WindSpeedMph.HasValue)
                    {
                        day.WindSpeedMph = official.WindSpeedMph;
                        day.WindDirectionDegrees = official.WindDirectionDegrees;
                    }
                    if (!string.IsNullOrWhiteSpace(official.Summary)) day.Summary = official.Summary;
                    if (!string.IsNullOrWhiteSpace(official.DetailedForecast))
                    {
                        day.DetailedForecast = official.DetailedForecast;
                    }
                    day.Source = "NWS";
                }
            }

            order.Sort();
            DateTime today = DateTime.Today;

            foreach (DateTime date in order)
            {
                if (date < today) continue;   // Open-Meteo can return the current day's past hours
                snapshot.Days.Add(byDate[date]);
            }
        }

        private static void BuildPrecipitation(
            WeatherSnapshot snapshot, OpenMeteoResult model,
            GridpointData grid, List<SevereOutlook> outlooks)
        {
            PrecipitationOutlook outlook = snapshot.Precipitation;

            outlook.Notes.Add(
                "Rain, snow and ice chances come from the NWS forecast grid where it reaches, "
                + "and from the Open-Meteo model beyond it.");
            outlook.Notes.Add(
                "Hail and severe-storm probabilities are SPC convective outlook values -- the chance of "
                + "severe weather within 25 miles. The SPC only issues them three days ahead.");

            foreach (ForecastDay day in snapshot.Days)
            {
                DateTime start = day.Date;
                DateTime end = start.AddDays(1);

                var entry = new PrecipitationDay
                {
                    Date = start,
                    AnyPrecipitationChance = day.PrecipitationProbability,
                    ExpectedPrecipitationInches = day.PrecipitationInches,
                    ExpectedSnowfallInches = day.SnowfallInches
                };

                // The raw NWS grid is more precise than the daily rollup where it reaches.
                if (grid != null)
                {
                    double? gridPop = grid.ProbabilityOfPrecipitation.MaxBetween(start, end);
                    if (gridPop.HasValue) entry.AnyPrecipitationChance = gridPop;

                    double? thunder = grid.ProbabilityOfThunder.MaxBetween(start, end);
                    if (thunder.HasValue) entry.ThunderstormChance = thunder;

                    double? snow = ToInches(grid.SnowfallAmount, grid.SnowfallAmount.SumBetween(start, end));
                    if (snow.HasValue) entry.ExpectedSnowfallInches = snow;

                    double? qpf = ToInches(grid.QuantitativePrecipitation,
                        grid.QuantitativePrecipitation.SumBetween(start, end));
                    if (qpf.HasValue) entry.ExpectedPrecipitationInches = qpf;

                    double? ice = ToInches(grid.IceAccumulation, grid.IceAccumulation.SumBetween(start, end));
                    if (ice.HasValue && ice.Value > 0.001)
                    {
                        entry.IcyChance = entry.AnyPrecipitationChance;
                    }
                }

                ApplyHourlyBreakdown(entry, snapshot.Hours, start, end);
                ApplySevereOutlook(entry, outlooks, start);

                outlook.Days.Add(entry);
            }
        }

        /// <summary>
        /// Splits the day's overall precipitation chance by type.
        ///
        /// No upstream publishes "chance of snow" as distinct from "chance of
        /// precipitation", so it is derived: for each hour the model expects snow,
        /// take that hour's precipitation probability, and the day's snow chance is
        /// the highest of them. That is an honest reading of "if it precipitates in
        /// this hour, it falls as snow" and it is why the UI labels these as
        /// derived rather than issued values.
        /// </summary>
        private static void ApplyHourlyBreakdown(
            PrecipitationDay entry, List<HourlyPoint> hours, DateTime start, DateTime end)
        {
            double? rain = null, snow = null, ice = null, thunder = null, cape = null;

            foreach (HourlyPoint hour in hours)
            {
                if (hour.Time < start || hour.Time >= end) continue;

                if (hour.CapeJoules.HasValue && (!cape.HasValue || hour.CapeJoules.Value > cape.Value))
                {
                    cape = hour.CapeJoules;
                }

                double probability = hour.PrecipitationProbability ?? 0d;
                if (probability <= 0d) continue;

                int code = hour.WeatherCode;
                bool snowing = WeatherCodes.IsSnow(code) || (hour.SnowfallInches ?? 0d) > 0.01d;
                bool freezing = WeatherCodes.IsFreezing(code);
                bool raining = WeatherCodes.IsRain(code) || (hour.RainInches ?? 0d) > 0.001d;

                if (snowing && (!snow.HasValue || probability > snow.Value)) snow = probability;
                if (freezing && (!ice.HasValue || probability > ice.Value)) ice = probability;
                if (raining && (!rain.HasValue || probability > rain.Value)) rain = probability;

                if (hour.IsThunder && (!thunder.HasValue || probability > thunder.Value))
                {
                    thunder = probability;
                }
            }

            entry.PeakCapeJoules = cape;
            entry.RainChance = rain;
            entry.SnowChance = snow;

            if (ice.HasValue && (!entry.IcyChance.HasValue || ice.Value > entry.IcyChance.Value))
            {
                entry.IcyChance = ice;
            }

            // Only fall back to the derived thunder chance when the forecast office
            // did not issue one of its own.
            if (!entry.ThunderstormChance.HasValue) entry.ThunderstormChance = thunder;

            // A day with a precipitation chance but no typed hours is still rain.
            if (!entry.RainChance.HasValue && !entry.SnowChance.HasValue && !entry.IcyChance.HasValue
                && entry.AnyPrecipitationChance.HasValue && entry.AnyPrecipitationChance.Value > 0)
            {
                entry.RainChance = entry.AnyPrecipitationChance;
            }
        }

        private static void ApplySevereOutlook(
            PrecipitationDay entry, List<SevereOutlook> outlooks, DateTime date)
        {
            if (outlooks == null) return;

            int dayNumber = (int)(date.Date - DateTime.Today).TotalDays + 1;
            if (dayNumber < 1 || dayNumber > 3) return;

            SevereOutlook match = outlooks.FirstOrDefault(o => o.Day == dayNumber);
            if (match == null) return;

            entry.SevereHailChance = match.HailProbability;
            entry.SevereWindChance = match.WindProbability;
            entry.TornadoChance = match.TornadoProbability;
            entry.SevereRiskCategory = match.Category;
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>
        /// Converts a grid value to inches based on the series' declared unit.
        /// NWS gridpoints report depth in millimetres regardless of the office.
        /// </summary>
        private static double? ToInches(GridSeries series, double? value)
        {
            if (!value.HasValue || series == null) return value;

            string uom = series.UnitOfMeasure ?? string.Empty;
            if (uom.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Units.MillimetresToInches(value.Value);
            }
            if (uom.IndexOf("cm", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Units.MillimetresToInches(value.Value * 10d);
            }
            if (uom.IndexOf("m]", StringComparison.OrdinalIgnoreCase) >= 0
                || uom.EndsWith(":m", StringComparison.OrdinalIgnoreCase))
            {
                return Units.MillimetresToInches(value.Value * 1000d);
            }
            return value;
        }

        private static DateTime Floor(DateTime value)
        {
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0);
        }

        /// <summary>
        /// Runs a fetch, turning a service failure into a warning and a null result
        /// so one dead upstream cannot empty the whole window.
        /// </summary>
        private static async Task<T> Try<T>(
            Func<Task<T>> factory, List<string> warnings, string label) where T : class
        {
            try
            {
                return await factory().ConfigureAwait(false);
            }
            catch (WeatherServiceException ex)
            {
                warnings.Add("Could not load the " + label + ": " + ex.Message);
                return null;
            }
        }
    }
}
