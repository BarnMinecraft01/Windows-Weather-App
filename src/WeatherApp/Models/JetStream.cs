using System;
using System.Collections.Generic;

namespace WeatherApp.Models
{
    /// <summary>One 250 hPa sample: where the jet is and how fast it is moving.</summary>
    public sealed class JetStreamPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        /// <summary>Wind speed at 250 hPa in knots -- the unit upper-air charts use.</summary>
        public double SpeedKnots { get; set; }

        /// <summary>Meteorological direction: the bearing the wind blows *from*.</summary>
        public double DirectionDegrees { get; set; }

        /// <summary>Height of the 250 hPa surface in metres. Troughs dip, ridges bulge.</summary>
        public double? GeopotentialHeightM { get; set; }

        public double? TemperatureC { get; set; }

        /// <summary>
        /// The conventional threshold for calling something "the jet stream" on a
        /// 250/300 hPa chart.
        /// </summary>
        public bool IsJetCore
        {
            get { return SpeedKnots >= 70d; }
        }
    }

    /// <summary>A gridded 250 hPa analysis over one region at one valid time.</summary>
    public sealed class JetStreamField
    {
        public JetStreamField()
        {
            Points = new List<JetStreamPoint>();
        }

        public List<JetStreamPoint> Points { get; private set; }

        public DateTime ValidTime { get; set; }
        public string ModelName { get; set; }

        public double SouthLatitude { get; set; }
        public double NorthLatitude { get; set; }
        public double WestLongitude { get; set; }
        public double EastLongitude { get; set; }

        public int Columns { get; set; }
        public int Rows { get; set; }

        public double MaxSpeedKnots { get; set; }

        /// <summary>
        /// Where the fastest wind in the field is. The UI calls this out because it
        /// is the single most useful number on the map: the jet core position drives
        /// storm tracks downstream.
        /// </summary>
        public JetStreamPoint StrongestPoint { get; set; }

        /// <summary>
        /// Grid lookup. Points are stored row-major from the north-west corner, which
        /// is the order the renderer walks them in.
        /// </summary>
        public JetStreamPoint At(int column, int row)
        {
            if (Columns <= 0 || Rows <= 0) return null;
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return null;

            int index = row * Columns + column;
            return index < Points.Count ? Points[index] : null;
        }
    }
}
