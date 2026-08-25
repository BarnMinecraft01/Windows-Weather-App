using System;
using System.Collections.Generic;
using System.IO;
using WeatherApp.Json;

namespace WeatherApp.Configuration
{
    /// <summary>
    /// Every upstream URL the app uses, in one place.
    ///
    /// NOAA reorganises image and GIS paths from time to time. Rather than bury
    /// those strings across a dozen service classes, they all live here and can be
    /// overridden at runtime by dropping an "endpoints.json" next to the settings
    /// file. That turns a broken product path into a text edit instead of a
    /// rebuild -- which matters for an app whose whole point is to keep working on
    /// machines that nobody wants to set a compiler up on.
    /// </summary>
    public static class Endpoints
    {
        // ---- National Weather Service (api.weather.gov) ------------------------
        // Keyless. Supplies the official forecast, the raw forecast grid, and alerts.
        public static string NwsApiBase = "https://api.weather.gov";

        /// <summary>NWS serves GeoJSON; this Accept header pins the schema version.</summary>
        public const string NwsAccept = "application/geo+json";

        // ---- Open-Meteo --------------------------------------------------------
        // Keyless. Supplies the 10-day extension beyond the NWS 7-day window, the
        // hourly precipitation-type breakdown, and the 250 hPa winds the jet stream
        // map is drawn from.
        public static string OpenMeteoForecast = "https://api.open-meteo.com/v1/forecast";
        public static string OpenMeteoGeocoding = "https://geocoding-api.open-meteo.com/v1/search";

        // ---- Radar imagery (RIDGE II) -----------------------------------------
        // {0} is a sector or four-letter station id, e.g. CONUS-LARGE or KSEW.
        // {1} is a frame index, 0 (most recent) through 9.
        public static string RadarFrameFormat = "https://radar.weather.gov/ridge/standard/{0}_{1}.gif";
        public static string RadarLoopFormat = "https://radar.weather.gov/ridge/standard/{0}_loop.gif";
        public static string RadarBrowserFormat = "https://radar.weather.gov/region/{0}/standard";

        // ---- Storm Prediction Center severe-weather outlooks -------------------
        // ArcGIS MapServer carrying the Day 1-3 categorical and probabilistic
        // (hail / wind / tornado) convective outlook polygons. The app queries it
        // point-in-polygon for the selected location.
        public static string SpcOutlookMapServer =
            "https://mapservices.weather.noaa.gov/vector/rest/services/outlooks/SPC_wx_outlks/MapServer";

        // ---- Hand-analysed upper-air charts ------------------------------------
        // Secondary to the rendered jet stream map. If a path 404s the panel says so
        // and offers a browser link rather than showing a broken image.
        public static string SpcUpperAirMaps = "https://www.spc.noaa.gov/obswx/maps/";
        public static string Chart300mb = "https://www.spc.noaa.gov/obswx/maps/300_0.gif";
        public static string Chart250mb = "https://www.spc.noaa.gov/obswx/maps/250_0.gif";

        /// <summary>
        /// Applies overrides from a JSON file of {"Chart300mb": "https://..."} pairs.
        /// Unknown keys are ignored so a config written for a later version does not
        /// stop this one from starting.
        /// </summary>
        public static void LoadOverrides(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            JsonValue root;
            try
            {
                if (!JsonValue.TryParse(File.ReadAllText(path), out root)) return;
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            if (!root.IsObject) return;

            foreach (string key in new List<string>(root.Keys))
            {
                string value = root[key].AsString(null);
                if (string.IsNullOrWhiteSpace(value)) continue;

                var field = typeof(Endpoints).GetField(key,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (field != null && field.FieldType == typeof(string) && !field.IsLiteral)
                {
                    field.SetValue(null, value.Trim());
                }
            }
        }

        /// <summary>Writes the current values out, to give the user a file to edit.</summary>
        public static void WriteTemplate(string path)
        {
            var lines = new List<string>();
            foreach (var field in typeof(Endpoints).GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.FieldType != typeof(string) || field.IsLiteral) continue;
                string value = (string)field.GetValue(null);
                lines.Add("  \"" + field.Name + "\": \"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            }

            string body = "{" + Environment.NewLine + string.Join("," + Environment.NewLine, lines)
                          + Environment.NewLine + "}" + Environment.NewLine;

            try
            {
                File.WriteAllText(path, body);
            }
            catch (IOException)
            {
                // Writing the template is a convenience, never a hard failure.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
