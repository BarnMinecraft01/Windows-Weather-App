using System.Collections.Generic;

namespace WeatherApp.Models
{
    /// <summary>A radar sector plus the label shown on its tab.</summary>
    public sealed class RadarSector
    {
        public RadarSector(string code, string title, string browserSlug)
        {
            Code = code;
            Title = title;
            BrowserSlug = browserSlug;
        }

        /// <summary>RIDGE II sector id used in the image path, e.g. "PACNORTHWEST".</summary>
        public string Code { get; private set; }

        public string Title { get; private set; }

        /// <summary>Slug for the full radar site, e.g. "pacnorthwest".</summary>
        public string BrowserSlug { get; private set; }

        public override string ToString() { return Title; }
    }

    /// <summary>
    /// A geographic area of interest: which radar sectors cover it, what box the
    /// jet stream map should be drawn over, and which states to pull alerts for.
    /// </summary>
    public sealed class Region
    {
        public Region(string id, string title)
        {
            Id = id;
            Title = title;
            RadarSectors = new List<RadarSector>();
            AlertAreas = new List<string>();
            DefaultLocations = new List<GeoLocation>();
        }

        public string Id { get; private set; }
        public string Title { get; private set; }

        public List<RadarSector> RadarSectors { get; private set; }

        /// <summary>NWS "area" codes (state / marine abbreviations) for alert queries.</summary>
        public List<string> AlertAreas { get; private set; }

        public List<GeoLocation> DefaultLocations { get; private set; }

        // Bounding box used by the jet stream renderer.
        public double SouthLatitude { get; set; }
        public double NorthLatitude { get; set; }
        public double WestLongitude { get; set; }
        public double EastLongitude { get; set; }

        /// <summary>Columns x rows of 250 hPa sample points fetched for this box.</summary>
        public int GridColumns { get; set; }
        public int GridRows { get; set; }

        public override string ToString() { return Title; }
    }
}
