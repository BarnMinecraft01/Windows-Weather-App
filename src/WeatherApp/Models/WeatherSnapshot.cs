using System;
using System.Collections.Generic;

namespace WeatherApp.Models
{
    /// <summary>
    /// Everything one refresh produced for one location.
    ///
    /// A refresh is deliberately partial-tolerant: if the SPC outlook is down but
    /// the NWS forecast came back, the snapshot carries the forecast and a note
    /// about what was missed, rather than failing whole.
    /// </summary>
    public sealed class WeatherSnapshot
    {
        public WeatherSnapshot()
        {
            Days = new List<ForecastDay>();
            Hours = new List<HourlyPoint>();
            Warnings = new List<string>();
            Precipitation = new PrecipitationOutlook();
            Alerts = new AlertCollection();
        }

        public GeoLocation Location { get; set; }
        public DateTime RetrievedAt { get; set; }

        public CurrentConditions Current { get; set; }
        public List<ForecastDay> Days { get; private set; }
        public List<HourlyPoint> Hours { get; private set; }
        public PrecipitationOutlook Precipitation { get; private set; }
        public AlertCollection Alerts { get; private set; }

        /// <summary>Non-fatal problems from this refresh, surfaced in the status bar.</summary>
        public List<string> Warnings { get; private set; }

        /// <summary>Which forecast office issued the NWS products, e.g. "NWS Seattle".</summary>
        public string ForecastOfficeName { get; set; }

        public string RadarStation { get; set; }

        public bool HasAnyData
        {
            get { return Current != null || Days.Count > 0 || Hours.Count > 0; }
        }
    }
}
