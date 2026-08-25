using System;
using System.Collections.Generic;

namespace WeatherApp.Models
{
    /// <summary>
    /// The chance of each precipitation type on a given day.
    ///
    /// No single upstream publishes all of these, so the values are assembled from
    /// three sources and each field records where it came from. Rain and snow
    /// probabilities come from the forecast grids; hail and severe-storm numbers
    /// come from the SPC convective outlook, which only reaches three days out --
    /// beyond that those fields are simply null and the UI shows a dash rather
    /// than inventing a number.
    /// </summary>
    public sealed class PrecipitationDay
    {
        public DateTime Date { get; set; }

        /// <summary>Chance of measurable precipitation of any type, 0-100.</summary>
        public double? AnyPrecipitationChance { get; set; }

        public double? RainChance { get; set; }
        public double? SnowChance { get; set; }
        public double? IcyChance { get; set; }
        public double? ThunderstormChance { get; set; }

        /// <summary>
        /// SPC probability of severe hail (1"+) within 25 miles, 0-100.
        /// Null beyond the Day 1-3 outlook window.
        /// </summary>
        public double? SevereHailChance { get; set; }

        public double? SevereWindChance { get; set; }
        public double? TornadoChance { get; set; }

        /// <summary>SPC categorical risk label, e.g. "Slight" or "Enhanced".</summary>
        public string SevereRiskCategory { get; set; }

        public double? ExpectedPrecipitationInches { get; set; }
        public double? ExpectedSnowfallInches { get; set; }
        public double? PeakCapeJoules { get; set; }

        public string DayLabel
        {
            get
            {
                DateTime today = DateTime.Today;
                if (Date.Date == today) return "Today";
                if (Date.Date == today.AddDays(1)) return "Tomorrow";
                return Date.ToString("ddd MMM d");
            }
        }

        /// <summary>The precipitation type most likely to actually show up.</summary>
        public string DominantType
        {
            get
            {
                double snow = SnowChance ?? 0;
                double ice = IcyChance ?? 0;
                double rain = RainChance ?? 0;
                double thunder = ThunderstormChance ?? 0;

                if (snow <= 0 && ice <= 0 && rain <= 0 && thunder <= 0) return "None";

                if (thunder >= 30 && thunder >= rain) return "Thunderstorms";
                if (ice >= snow && ice >= rain && ice > 0) return "Freezing rain";
                if (snow >= rain && snow > 0) return "Snow";
                if (rain > 0) return "Rain";
                return "None";
            }
        }
    }

    public sealed class PrecipitationOutlook
    {
        public PrecipitationOutlook()
        {
            Days = new List<PrecipitationDay>();
            Notes = new List<string>();
        }

        public List<PrecipitationDay> Days { get; private set; }

        /// <summary>Caveats worth showing, e.g. that the SPC outlook stops at Day 3.</summary>
        public List<string> Notes { get; private set; }
    }
}
