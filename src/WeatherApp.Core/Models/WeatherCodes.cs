using System.Collections.Generic;

namespace WeatherApp.Models
{
    /// <summary>
    /// WMO 4677 present-weather codes, which is what Open-Meteo reports.
    /// Used for the extended-forecast days that fall outside the NWS window and
    /// therefore have no NWS narrative to show.
    /// </summary>
    public static class WeatherCodes
    {
        private static readonly Dictionary<int, string> Descriptions = new Dictionary<int, string>
        {
            { 0,  "Clear" },
            { 1,  "Mainly clear" },
            { 2,  "Partly cloudy" },
            { 3,  "Overcast" },
            { 45, "Fog" },
            { 48, "Freezing fog" },
            { 51, "Light drizzle" },
            { 53, "Drizzle" },
            { 55, "Heavy drizzle" },
            { 56, "Light freezing drizzle" },
            { 57, "Freezing drizzle" },
            { 61, "Light rain" },
            { 63, "Rain" },
            { 65, "Heavy rain" },
            { 66, "Light freezing rain" },
            { 67, "Freezing rain" },
            { 71, "Light snow" },
            { 73, "Snow" },
            { 75, "Heavy snow" },
            { 77, "Snow grains" },
            { 80, "Light rain showers" },
            { 81, "Rain showers" },
            { 82, "Violent rain showers" },
            { 85, "Light snow showers" },
            { 86, "Snow showers" },
            { 95, "Thunderstorms" },
            { 96, "Thunderstorms with hail" },
            { 99, "Thunderstorms with heavy hail" }
        };

        public static string Describe(int code)
        {
            string description;
            return Descriptions.TryGetValue(code, out description) ? description : "Unknown";
        }

        public static bool IsSnow(int code)
        {
            return code == 71 || code == 73 || code == 75 || code == 77 || code == 85 || code == 86;
        }

        public static bool IsFreezing(int code)
        {
            return code == 48 || code == 56 || code == 57 || code == 66 || code == 67;
        }

        public static bool IsThunder(int code)
        {
            return code == 95 || code == 96 || code == 99;
        }

        public static bool IsHail(int code)
        {
            return code == 96 || code == 99;
        }

        public static bool IsRain(int code)
        {
            return (code >= 51 && code <= 55)
                || (code >= 61 && code <= 65)
                || (code >= 80 && code <= 82);
        }

        /// <summary>
        /// A short glyph for the condition. Deliberately limited to characters that
        /// render in the fonts shipped with Windows 7, where colour emoji do not exist.
        /// </summary>
        public static string Glyph(int code, bool isDaytime)
        {
            if (IsThunder(code)) return "⚡";
            if (IsHail(code)) return "⚡";
            if (IsSnow(code)) return "❄";
            if (IsFreezing(code)) return "❄";
            if (IsRain(code)) return "☂";
            if (code == 45 || code == 48) return "≡";
            if (code == 3) return "☁";
            if (code == 1 || code == 2) return isDaytime ? "⛅" : "☾";
            if (code == 0) return isDaytime ? "☀" : "☾";
            return "•";
        }
    }
}
