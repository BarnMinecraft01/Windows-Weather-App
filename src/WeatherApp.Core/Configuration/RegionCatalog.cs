using System;
using System.Collections.Generic;
using System.Linq;
using WeatherApp.Models;

namespace WeatherApp.Configuration
{
    /// <summary>
    /// The four areas of interest the app is built around, plus a nationwide view.
    ///
    /// A region bundles three things that otherwise have to be looked up separately:
    /// which RIDGE II radar sectors cover it, which state codes to ask the NWS for
    /// alerts in, and what latitude/longitude box the jet stream map should be
    /// drawn over.
    /// </summary>
    public static class RegionCatalog
    {
        private static readonly List<Region> All = BuildRegions();

        public static IList<Region> Regions
        {
            get { return All; }
        }

        public static Region Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return All[0];

            Region match = All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            return match ?? All[0];
        }

        /// <summary>Picks the region whose box contains the point, falling back to CONUS.</summary>
        public static Region ForLocation(GeoLocation location)
        {
            if (location == null) return Find("conus");

            foreach (Region region in All)
            {
                bool inside = location.Latitude >= region.SouthLatitude
                              && location.Latitude <= region.NorthLatitude
                              && location.Longitude >= region.WestLongitude
                              && location.Longitude <= region.EastLongitude;

                // The nationwide box overlaps the others, so let a tighter region win.
                if (inside && !string.Equals(region.Id, "conus", StringComparison.OrdinalIgnoreCase))
                {
                    return region;
                }
            }
            return Find("conus");
        }

        private static List<Region> BuildRegions()
        {
            var regions = new List<Region>();

            // ---- Pacific Northwest coast and Hawaii ----------------------------
            // One box rather than two: it spans the North Pacific storm track that
            // feeds both, which is exactly what the 250 hPa map should show.
            var pacific = new Region("pnw-hawaii", "PNW Coast & Hawaii")
            {
                SouthLatitude = 17.0,
                NorthLatitude = 52.0,
                WestLongitude = -163.0,
                EastLongitude = -115.0,
                GridColumns = 12,
                GridRows = 10
            };
            pacific.RadarSectors.Add(new RadarSector("PACNORTHWEST", "Pacific Northwest", "pacnorthwest"));
            pacific.RadarSectors.Add(new RadarSector("HAWAII", "Hawaii", "hawaii"));
            pacific.RadarSectors.Add(new RadarSector("PACSOUTHWEST", "Pacific Southwest", "pacsouthwest"));
            pacific.AlertAreas.AddRange(new[] { "WA", "OR", "HI" });
            pacific.DefaultLocations.AddRange(new[]
            {
                Place("Seattle", "WA", 47.6062, -122.3321),
                Place("Portland", "OR", 45.5152, -122.6784),
                Place("Astoria", "OR", 46.1879, -123.8313),
                Place("Aberdeen", "WA", 46.9754, -123.8157),
                Place("Newport", "OR", 44.6368, -124.0535),
                Place("Honolulu", "HI", 21.3069, -157.8583),
                Place("Hilo", "HI", 19.7297, -155.0900),
                Place("Kahului", "HI", 20.8893, -156.4729)
            });
            regions.Add(pacific);

            // ---- Atlantic seaboard ---------------------------------------------
            var atlantic = new Region("east-atlantic", "East Atlantic Coast")
            {
                SouthLatitude = 24.0,
                NorthLatitude = 48.0,
                WestLongitude = -85.0,
                EastLongitude = -64.0,
                GridColumns = 10,
                GridRows = 9
            };
            atlantic.RadarSectors.Add(new RadarSector("NORTHEAST", "Northeast", "northeast"));
            atlantic.RadarSectors.Add(new RadarSector("SOUTHEAST", "Southeast", "southeast"));
            atlantic.AlertAreas.AddRange(new[]
            {
                "ME", "NH", "MA", "RI", "CT", "NY", "NJ", "PA",
                "DE", "MD", "DC", "VA", "NC", "SC", "GA", "FL"
            });
            atlantic.DefaultLocations.AddRange(new[]
            {
                Place("Portland", "ME", 43.6591, -70.2568),
                Place("Boston", "MA", 42.3601, -71.0589),
                Place("New York", "NY", 40.7128, -74.0060),
                Place("Atlantic City", "NJ", 39.3643, -74.4229),
                Place("Ocean City", "MD", 38.3365, -75.0849),
                Place("Virginia Beach", "VA", 36.8529, -75.9780),
                Place("Cape Hatteras", "NC", 35.2510, -75.5288),
                Place("Charleston", "SC", 32.7765, -79.9311),
                Place("Jacksonville", "FL", 30.3322, -81.6557),
                Place("Miami", "FL", 25.7617, -80.1918)
            });
            regions.Add(atlantic);

            // ---- Lower 48 --------------------------------------------------------
            var conus = new Region("conus", "Contiguous US")
            {
                SouthLatitude = 23.0,
                NorthLatitude = 51.0,
                WestLongitude = -126.0,
                EastLongitude = -65.0,
                GridColumns = 14,
                GridRows = 8
            };
            conus.RadarSectors.Add(new RadarSector("CONUS-LARGE", "Lower 48 (large)", "conus-large"));
            conus.RadarSectors.Add(new RadarSector("CONUS", "Lower 48", "conus"));
            conus.RadarSectors.Add(new RadarSector("UPPERMISSVLY", "Upper Mississippi Valley", "uppermissvly"));
            conus.RadarSectors.Add(new RadarSector("SOUTHPLAINS", "Southern Plains", "southplains"));
            conus.RadarSectors.Add(new RadarSector("NORTHROCKIES", "Northern Rockies", "northrockies"));
            conus.AlertAreas.AddRange(new[]
            {
                "AL", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA",
                "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA",
                "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM",
                "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD",
                "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY"
            });
            conus.DefaultLocations.AddRange(new[]
            {
                Place("Chicago", "IL", 41.8781, -87.6298),
                Place("Denver", "CO", 39.7392, -104.9903),
                Place("Dallas", "TX", 32.7767, -96.7970),
                Place("Kansas City", "MO", 39.0997, -94.5786),
                Place("Oklahoma City", "OK", 35.4676, -97.5164),
                Place("Minneapolis", "MN", 44.9778, -93.2650),
                Place("Atlanta", "GA", 33.7490, -84.3880),
                Place("Phoenix", "AZ", 33.4484, -112.0740),
                Place("Los Angeles", "CA", 34.0522, -118.2437),
                Place("Salt Lake City", "UT", 40.7608, -111.8910)
            });
            regions.Add(conus);

            // ---- Alaska ----------------------------------------------------------
            var alaska = new Region("alaska", "Alaska")
            {
                SouthLatitude = 51.0,
                NorthLatitude = 72.0,
                WestLongitude = -172.0,
                EastLongitude = -129.0,
                GridColumns = 12,
                GridRows = 8
            };
            alaska.RadarSectors.Add(new RadarSector("ALASKA", "Alaska", "alaska"));
            alaska.AlertAreas.Add("AK");
            alaska.DefaultLocations.AddRange(new[]
            {
                Place("Anchorage", "AK", 61.2181, -149.9003),
                Place("Fairbanks", "AK", 64.8378, -147.7164),
                Place("Juneau", "AK", 58.3019, -134.4197),
                Place("Nome", "AK", 64.5011, -165.4064),
                Place("Utqiagvik", "AK", 71.2906, -156.7886),
                Place("Kodiak", "AK", 57.7900, -152.4072),
                Place("Bethel", "AK", 60.7922, -161.7558)
            });
            regions.Add(alaska);

            return regions;
        }

        private static GeoLocation Place(string name, string state, double latitude, double longitude)
        {
            return new GeoLocation
            {
                Name = name,
                Admin = state,
                Country = "United States",
                Latitude = latitude,
                Longitude = longitude
            };
        }
    }
}
