using System;
using System.Collections.Generic;
using System.Globalization;
using WeatherApp.Json;

namespace WeatherApp.Services
{
    /// <summary>One validTime/value pair from an NWS gridpoint series.</summary>
    public struct GridSample
    {
        public DateTime Start;
        public DateTime End;
        public double Value;
    }

    /// <summary>
    /// A single layer of the NWS raw forecast grid, e.g. snowfallAmount.
    ///
    /// These layers are the reason the app talks to /gridpoints at all: the plain
    /// /forecast endpoint gives prose, while the grid gives separate quantitative
    /// series for snow, ice, rainfall and (where the office issues it) thunder
    /// probability -- which is what the precipitation tab needs.
    ///
    /// Each entry carries an ISO-8601 interval rather than an instant, so a single
    /// value can cover anything from one hour to a whole day and the samples must
    /// be queried by time range rather than by index.
    /// </summary>
    public sealed class GridSeries
    {
        private readonly List<GridSample> _samples = new List<GridSample>();

        public string UnitOfMeasure { get; private set; }

        public int Count { get { return _samples.Count; } }

        public IEnumerable<GridSample> Samples { get { return _samples; } }

        public static GridSeries Parse(JsonValue node)
        {
            var series = new GridSeries();
            if (node == null || !node.Exists) return series;

            series.UnitOfMeasure = node["uom"].AsString(string.Empty);

            foreach (JsonValue entry in node["values"].Items)
            {
                string validTime = entry["validTime"].AsString(null);
                double? value = entry["value"].AsNullableDouble();

                if (string.IsNullOrEmpty(validTime) || !value.HasValue) continue;

                DateTime start;
                TimeSpan duration;
                if (!TryParseInterval(validTime, out start, out duration)) continue;

                series._samples.Add(new GridSample
                {
                    Start = start,
                    End = start.Add(duration),
                    Value = value.Value
                });
            }

            series._samples.Sort((a, b) => a.Start.CompareTo(b.Start));
            return series;
        }

        /// <summary>The value covering the given instant, or null if the grid does not reach it.</summary>
        public double? ValueAt(DateTime when)
        {
            foreach (GridSample sample in _samples)
            {
                if (when >= sample.Start && when < sample.End) return sample.Value;
            }
            return null;
        }

        /// <summary>Largest value overlapping the window. Used for "chance of X today".</summary>
        public double? MaxBetween(DateTime start, DateTime end)
        {
            double? max = null;
            foreach (GridSample sample in _samples)
            {
                if (sample.End <= start || sample.Start >= end) continue;
                if (!max.HasValue || sample.Value > max.Value) max = sample.Value;
            }
            return max;
        }

        /// <summary>
        /// Accumulation over the window. Samples are weighted by how much of their
        /// interval falls inside it, so a six-hour snowfall total straddling midnight
        /// is split between the two days rather than counted twice.
        /// </summary>
        public double? SumBetween(DateTime start, DateTime end)
        {
            double total = 0d;
            bool any = false;

            foreach (GridSample sample in _samples)
            {
                if (sample.End <= start || sample.Start >= end) continue;

                DateTime overlapStart = sample.Start > start ? sample.Start : start;
                DateTime overlapEnd = sample.End < end ? sample.End : end;

                double sampleMinutes = (sample.End - sample.Start).TotalMinutes;
                double overlapMinutes = (overlapEnd - overlapStart).TotalMinutes;
                if (sampleMinutes <= 0 || overlapMinutes <= 0) continue;

                total += sample.Value * (overlapMinutes / sampleMinutes);
                any = true;
            }

            return any ? total : (double?)null;
        }

        /// <summary>
        /// Splits "2026-08-25T14:00:00+00:00/PT6H" into its instant and duration.
        /// </summary>
        internal static bool TryParseInterval(string text, out DateTime start, out TimeSpan duration)
        {
            start = DateTime.MinValue;
            duration = TimeSpan.FromHours(1);

            if (string.IsNullOrEmpty(text)) return false;

            int slash = text.IndexOf('/');
            string instant = slash > 0 ? text.Substring(0, slash) : text;

            // Keep the office's own wall clock, matching JsonValue.AsDateTime, so grid
            // samples line up with the forecast periods they describe.
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(instant, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
            {
                return false;
            }
            start = parsed.DateTime;

            if (slash > 0 && slash + 1 < text.Length)
            {
                duration = ParseIso8601Duration(text.Substring(slash + 1));
            }
            return true;
        }

        /// <summary>
        /// Minimal ISO-8601 duration reader covering the forms NWS actually emits:
        /// PT1H, PT6H, P1D, P1DT12H. Months and years never appear in a gridpoint.
        /// </summary>
        internal static TimeSpan ParseIso8601Duration(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != 'P') return TimeSpan.FromHours(1);

            int days = 0, hours = 0, minutes = 0, seconds = 0;
            bool inTimeSection = false;
            int number = 0;
            bool haveNumber = false;

            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];

                if (c == 'T')
                {
                    inTimeSection = true;
                    number = 0;
                    haveNumber = false;
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    number = number * 10 + (c - '0');
                    haveNumber = true;
                    continue;
                }

                if (!haveNumber) continue;

                switch (c)
                {
                    case 'D': days = number; break;
                    case 'W': days = number * 7; break;
                    case 'H': hours = number; break;
                    case 'M': if (inTimeSection) minutes = number; break;
                    case 'S': seconds = number; break;
                }

                number = 0;
                haveNumber = false;
            }

            var duration = new TimeSpan(days, hours, minutes, seconds);
            return duration <= TimeSpan.Zero ? TimeSpan.FromHours(1) : duration;
        }
    }
}
