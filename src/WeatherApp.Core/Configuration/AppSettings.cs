using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WeatherApp.Json;
using WeatherApp.Models;
using WeatherApp.Platform;

namespace WeatherApp.Configuration
{
    /// <summary>
    /// User preferences and saved places, stored as JSON under %APPDATA%.
    ///
    /// Written by hand rather than through a serializer so the file stays readable
    /// and hand-editable, and so a settings file from a newer build never stops an
    /// older one from starting.
    /// </summary>
    public sealed class AppSettings
    {
        public AppSettings()
        {
            SavedLocations = new List<GeoLocation>();
            RegionId = "conus";
            UseMetric = false;
            AutoRefreshMinutes = 15;
            NotifyOnWarnings = true;
            AlertScope = "region";
            ContactForUserAgent = string.Empty;
            LastRadarSector = string.Empty;
            JetStreamLevel = 250;
        }

        public List<GeoLocation> SavedLocations { get; private set; }

        /// <summary>Index into SavedLocations of the place currently shown.</summary>
        public int ActiveLocationIndex { get; set; }

        public string RegionId { get; set; }
        public bool UseMetric { get; set; }
        public int AutoRefreshMinutes { get; set; }
        public bool NotifyOnWarnings { get; set; }

        /// <summary>"point" for alerts covering the saved place only, "region" for the whole area.</summary>
        public string AlertScope { get; set; }

        /// <summary>
        /// Optional e-mail or site the NWS can use to identify heavy callers. Their
        /// API docs ask for it, and supplying one makes throttling far less likely.
        /// </summary>
        public string ContactForUserAgent { get; set; }

        public string LastRadarSector { get; set; }

        /// <summary>Pressure level for the jet stream map: 250 hPa or 300 hPa.</summary>
        public int JetStreamLevel { get; set; }

        public GeoLocation ActiveLocation
        {
            get
            {
                if (SavedLocations.Count == 0) return null;

                int index = ActiveLocationIndex;
                if (index < 0 || index >= SavedLocations.Count) index = 0;
                return SavedLocations[index];
            }
        }

        // ---- Storage ---------------------------------------------------------

        // Resolved per platform; see WeatherApp.Platform.AppPaths for why these are
        // not simply SpecialFolder.ApplicationData on every OS.
        public static string DirectoryPath
        {
            get { return AppPaths.SettingsDirectory; }
        }

        public static string SettingsPath
        {
            get { return AppPaths.SettingsFile; }
        }

        public static string EndpointsPath
        {
            get { return AppPaths.EndpointsFile; }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();

            try
            {
                if (!File.Exists(SettingsPath))
                {
                    settings.SeedDefaults();
                    return settings;
                }

                JsonValue root;
                if (!JsonValue.TryParse(File.ReadAllText(SettingsPath), out root) || !root.IsObject)
                {
                    settings.SeedDefaults();
                    return settings;
                }

                settings.RegionId = root["regionId"].AsString(settings.RegionId);
                settings.UseMetric = root["useMetric"].AsBool(settings.UseMetric);
                settings.AutoRefreshMinutes = root["autoRefreshMinutes"].AsInt(settings.AutoRefreshMinutes);
                settings.NotifyOnWarnings = root["notifyOnWarnings"].AsBool(settings.NotifyOnWarnings);
                settings.AlertScope = root["alertScope"].AsString(settings.AlertScope);
                settings.ContactForUserAgent = root["contact"].AsString(string.Empty);
                settings.LastRadarSector = root["lastRadarSector"].AsString(string.Empty);
                settings.JetStreamLevel = root["jetStreamLevel"].AsInt(250);
                settings.ActiveLocationIndex = root["activeLocationIndex"].AsInt(0);

                foreach (JsonValue item in root["locations"].Items)
                {
                    var location = new GeoLocation
                    {
                        Name = item["name"].AsString(string.Empty),
                        Admin = item["admin"].AsString(string.Empty),
                        Country = item["country"].AsString("United States"),
                        Latitude = item["lat"].AsDouble(),
                        Longitude = item["lon"].AsDouble(),
                        TimeZone = item["timeZone"].AsString(null)
                    };

                    // Coordinates of exactly 0,0 mean a corrupt entry, not the Atlantic.
                    if (Math.Abs(location.Latitude) < 0.0001 && Math.Abs(location.Longitude) < 0.0001) continue;

                    settings.SavedLocations.Add(location);
                }

                if (settings.SavedLocations.Count == 0) settings.SeedDefaults();
                if (settings.AutoRefreshMinutes < 5) settings.AutoRefreshMinutes = 5;
                if (settings.JetStreamLevel != 250 && settings.JetStreamLevel != 300)
                {
                    settings.JetStreamLevel = 250;
                }
            }
            catch (IOException)
            {
                settings.SeedDefaults();
            }
            catch (UnauthorizedAccessException)
            {
                settings.SeedDefaults();
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                AppPaths.EnsureDirectory();

                var builder = new StringBuilder();
                builder.AppendLine("{");
                builder.AppendLine("  \"regionId\": " + Quote(RegionId) + ",");
                builder.AppendLine("  \"useMetric\": " + (UseMetric ? "true" : "false") + ",");
                builder.AppendLine("  \"autoRefreshMinutes\": " + Number(AutoRefreshMinutes) + ",");
                builder.AppendLine("  \"notifyOnWarnings\": " + (NotifyOnWarnings ? "true" : "false") + ",");
                builder.AppendLine("  \"alertScope\": " + Quote(AlertScope) + ",");
                builder.AppendLine("  \"contact\": " + Quote(ContactForUserAgent) + ",");
                builder.AppendLine("  \"lastRadarSector\": " + Quote(LastRadarSector) + ",");
                builder.AppendLine("  \"jetStreamLevel\": " + Number(JetStreamLevel) + ",");
                builder.AppendLine("  \"activeLocationIndex\": " + Number(ActiveLocationIndex) + ",");
                builder.AppendLine("  \"locations\": [");

                for (int i = 0; i < SavedLocations.Count; i++)
                {
                    GeoLocation location = SavedLocations[i];
                    builder.Append("    { \"name\": ").Append(Quote(location.Name))
                           .Append(", \"admin\": ").Append(Quote(location.Admin))
                           .Append(", \"country\": ").Append(Quote(location.Country))
                           .Append(", \"lat\": ").Append(Number(location.Latitude))
                           .Append(", \"lon\": ").Append(Number(location.Longitude))
                           .Append(", \"timeZone\": ").Append(Quote(location.TimeZone))
                           .Append(" }")
                           .AppendLine(i < SavedLocations.Count - 1 ? "," : string.Empty);
                }

                builder.AppendLine("  ]");
                builder.AppendLine("}");

                // Write to a sibling file first so a crash mid-write cannot leave an
                // unreadable settings file behind.
                string temporary = SettingsPath + ".tmp";
                File.WriteAllText(temporary, builder.ToString(), Encoding.UTF8);

                if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                File.Move(temporary, SettingsPath);
            }
            catch (IOException)
            {
                // Preferences are a convenience; failing to persist them must not
                // interrupt the user.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Starts a fresh install off with somewhere sensible to look at.</summary>
        private void SeedDefaults()
        {
            SavedLocations.Clear();

            Region region = RegionCatalog.Find(RegionId);
            int take = Math.Min(4, region.DefaultLocations.Count);
            for (int i = 0; i < take; i++)
            {
                SavedLocations.Add(region.DefaultLocations[i]);
            }

            ActiveLocationIndex = 0;
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";

            var builder = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }

        private static string Number(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
