using System;

namespace WeatherApp.Models
{
    /// <summary>Right-now conditions, blended from the NWS forecast grid and Open-Meteo.</summary>
    public sealed class CurrentConditions
    {
        public DateTime ObservedAt { get; set; }
        public double? TemperatureF { get; set; }
        public double? FeelsLikeF { get; set; }
        public double? DewPointF { get; set; }
        public double? RelativeHumidity { get; set; }
        public double? WindSpeedMph { get; set; }
        public double? WindGustMph { get; set; }
        public double? WindDirectionDegrees { get; set; }
        public double? PressureMb { get; set; }
        public double? VisibilityMiles { get; set; }
        public double? CloudCoverPercent { get; set; }
        public double? PrecipitationRateInches { get; set; }
        public int WeatherCode { get; set; }
        public bool IsDaytime { get; set; }

        /// <summary>Plain-language summary, preferring the NWS wording when present.</summary>
        public string Summary { get; set; }

        public string Source { get; set; }

        public string WindDirectionCardinal
        {
            get { return Units.DegreesToCardinal(WindDirectionDegrees); }
        }
    }

    /// <summary>One hour of forecast detail.</summary>
    public sealed class HourlyPoint
    {
        public DateTime Time { get; set; }
        public double? TemperatureF { get; set; }
        public double? FeelsLikeF { get; set; }
        public double? PrecipitationProbability { get; set; }
        public double? PrecipitationInches { get; set; }
        public double? RainInches { get; set; }
        public double? SnowfallInches { get; set; }
        public double? WindSpeedMph { get; set; }
        public double? WindGustMph { get; set; }
        public double? WindDirectionDegrees { get; set; }
        public double? RelativeHumidity { get; set; }
        public double? CapeJoules { get; set; }
        public double? CloudCoverPercent { get; set; }
        public int WeatherCode { get; set; }
        public string Summary { get; set; }

        public bool IsThunder
        {
            get
            {
                if (WeatherCode == 95 || WeatherCode == 96 || WeatherCode == 99) return true;
                return !string.IsNullOrEmpty(Summary)
                    && Summary.IndexOf("thunder", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>WMO codes 96 and 99 are "thunderstorm with hail".</summary>
        public bool IsHail
        {
            get { return WeatherCode == 96 || WeatherCode == 99; }
        }
    }

    /// <summary>One day of the extended forecast.</summary>
    public sealed class ForecastDay
    {
        public DateTime Date { get; set; }
        public double? HighF { get; set; }
        public double? LowF { get; set; }
        public double? PrecipitationProbability { get; set; }
        public double? PrecipitationInches { get; set; }
        public double? RainInches { get; set; }
        public double? SnowfallInches { get; set; }
        public double? WindSpeedMph { get; set; }
        public double? WindGustMph { get; set; }
        public double? WindDirectionDegrees { get; set; }
        public double? UvIndexMax { get; set; }
        public DateTime? Sunrise { get; set; }
        public DateTime? Sunset { get; set; }
        public int WeatherCode { get; set; }

        /// <summary>NWS narrative when available, otherwise derived from the WMO code.</summary>
        public string Summary { get; set; }
        public string DetailedForecast { get; set; }

        /// <summary>Which upstream produced this day. Shown so the user knows what they are reading.</summary>
        public string Source { get; set; }

        public string DayLabel
        {
            get
            {
                DateTime today = DateTime.Today;
                if (Date.Date == today) return "Today";
                if (Date.Date == today.AddDays(1)) return "Tomorrow";
                return Date.ToString("ddd");
            }
        }
    }
}
