using System;
using System.Globalization;

namespace WeatherApp.Models
{
    /// <summary>
    /// Unit conversion and formatting.
    ///
    /// Everything inside the app is stored in US customary units, because that is
    /// what the NWS products are issued in and what the intended audience reads.
    /// Metric is a presentation-time conversion applied by the formatters below.
    /// </summary>
    public static class Units
    {
        private static readonly string[] Cardinals =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
        };

        /// <summary>When true, formatters emit Celsius / km/h / mm.</summary>
        public static bool UseMetric { get; set; }

        public static double CelsiusToFahrenheit(double celsius)
        {
            return celsius * 9d / 5d + 32d;
        }

        public static double FahrenheitToCelsius(double fahrenheit)
        {
            return (fahrenheit - 32d) * 5d / 9d;
        }

        public static double MillimetresToInches(double mm)
        {
            return mm / 25.4d;
        }

        public static double InchesToMillimetres(double inches)
        {
            return inches * 25.4d;
        }

        public static double KilometresPerHourToMph(double kph)
        {
            return kph * 0.621371d;
        }

        public static double MphToKilometresPerHour(double mph)
        {
            return mph / 0.621371d;
        }

        public static double MetresToFeet(double metres)
        {
            return metres * 3.28084d;
        }

        public static string DegreesToCardinal(double? degrees)
        {
            if (!degrees.HasValue) return "--";

            double normalised = degrees.Value % 360d;
            if (normalised < 0) normalised += 360d;

            int index = (int)Math.Round(normalised / 22.5d) % 16;
            return Cardinals[index];
        }

        public static string FormatTemperature(double? fahrenheit, string fallback = "--")
        {
            if (!fahrenheit.HasValue) return fallback;

            double value = UseMetric ? FahrenheitToCelsius(fahrenheit.Value) : fahrenheit.Value;
            return Math.Round(value).ToString("0", CultureInfo.CurrentCulture) + "°";
        }

        public static string TemperatureUnitLabel
        {
            get { return UseMetric ? "°C" : "°F"; }
        }

        public static string FormatSpeed(double? mph, string fallback = "--")
        {
            if (!mph.HasValue) return fallback;

            if (UseMetric)
            {
                return Math.Round(MphToKilometresPerHour(mph.Value)).ToString("0", CultureInfo.CurrentCulture) + " km/h";
            }
            return Math.Round(mph.Value).ToString("0", CultureInfo.CurrentCulture) + " mph";
        }

        public static string FormatPrecipitation(double? inches, string fallback = "--")
        {
            if (!inches.HasValue) return fallback;

            if (UseMetric)
            {
                double mm = InchesToMillimetres(inches.Value);
                return mm.ToString(mm < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " mm";
            }

            if (inches.Value > 0 && inches.Value < 0.01) return "trace";
            return inches.Value.ToString("0.00", CultureInfo.CurrentCulture) + "\"";
        }

        public static string FormatSnow(double? inches, string fallback = "--")
        {
            if (!inches.HasValue) return fallback;

            if (UseMetric)
            {
                double cm = InchesToMillimetres(inches.Value) / 10d;
                return cm.ToString(cm < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " cm";
            }

            if (inches.Value > 0 && inches.Value < 0.1) return "trace";
            return inches.Value.ToString("0.0", CultureInfo.CurrentCulture) + "\"";
        }

        public static string FormatPercent(double? value, string fallback = "--")
        {
            if (!value.HasValue) return fallback;
            return Math.Round(value.Value).ToString("0", CultureInfo.CurrentCulture) + "%";
        }

        public static string FormatPressure(double? millibars, string fallback = "--")
        {
            if (!millibars.HasValue) return fallback;

            if (UseMetric)
            {
                return Math.Round(millibars.Value).ToString("0", CultureInfo.CurrentCulture) + " hPa";
            }

            double inchesOfMercury = millibars.Value * 0.0295299830714d;
            return inchesOfMercury.ToString("0.00", CultureInfo.CurrentCulture) + " inHg";
        }

        public static string FormatDistance(double? miles, string fallback = "--")
        {
            if (!miles.HasValue) return fallback;

            if (UseMetric)
            {
                double km = miles.Value * 1.609344d;
                return km.ToString(km < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " km";
            }
            return miles.Value.ToString(miles.Value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " mi";
        }
    }
}
