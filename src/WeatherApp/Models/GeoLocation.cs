using System;
using System.Globalization;

namespace WeatherApp.Models
{
    /// <summary>A saved place the user wants weather for.</summary>
    public sealed class GeoLocation
    {
        public string Name { get; set; }
        public string Admin { get; set; }        // State or province
        public string Country { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string TimeZone { get; set; }

        /// <summary>NWS forecast office, e.g. "SEW". Cached from the /points lookup.</summary>
        public string ForecastOffice { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }

        /// <summary>Nearest NEXRAD site, e.g. "KATX". Used for the local radar view.</summary>
        public string RadarStation { get; set; }

        /// <summary>NWS county/forecast zone id, used to scope alerts tightly.</summary>
        public string ZoneId { get; set; }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return Coordinates;
                if (string.IsNullOrWhiteSpace(Admin)) return Name;
                return Name + ", " + Admin;
            }
        }

        public string Coordinates
        {
            get
            {
                return Latitude.ToString("0.####", CultureInfo.InvariantCulture) + ", "
                     + Longitude.ToString("0.####", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The NWS API rejects coordinates carrying more than four decimal places,
        /// so requests are built from this rounded form.
        /// </summary>
        public string ApiCoordinates
        {
            get
            {
                return Math.Round(Latitude, 4).ToString("0.####", CultureInfo.InvariantCulture) + ","
                     + Math.Round(Longitude, 4).ToString("0.####", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// True for places api.weather.gov actually covers: the 50 states and the
        /// US territories. Outside that the app falls back to Open-Meteo alone and
        /// says so, rather than showing an empty forecast.
        /// </summary>
        public bool IsNwsCovered
        {
            get
            {
                return string.IsNullOrWhiteSpace(Country)
                    || Country.Equals("United States", StringComparison.OrdinalIgnoreCase)
                    || Country.Equals("US", StringComparison.OrdinalIgnoreCase)
                    || Country.Equals("USA", StringComparison.OrdinalIgnoreCase);
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
