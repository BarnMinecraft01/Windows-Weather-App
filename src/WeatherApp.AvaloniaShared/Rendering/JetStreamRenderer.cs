using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI.Rendering
{
    /// <summary>
    /// Draws a gridded upper-air wind field.
    ///
    /// This lives in the shared Avalonia layer because it is the most involved
    /// drawing in the app and both the desktop and phone heads show the same map.
    /// Duplicating it would mean the two builds could quietly disagree about where
    /// the jet is, which is exactly the kind of divergence worth engineering out.
    ///
    /// The projection is equirectangular with the horizontal axis scaled by the
    /// cosine of the box's centre latitude. Not conformal, and not trying to be:
    /// it keeps shapes roughly right over a region-sized box while staying simple
    /// enough that the grid maths is obviously correct.
    /// </summary>
    public static class JetStreamRenderer
    {
        public const double LegendHeight = 28;
        public const double CompactLegendHeight = 22;

        /// <summary>
        /// Renders the field into <paramref name="host"/>.
        /// </summary>
        /// <param name="compact">
        /// Phone layout: thinner legend, no graticule labels, and place names only
        /// for the selected location. A 390pt screen cannot carry the desktop
        /// annotation density without becoming unreadable.
        /// </param>
        public static void Draw(DrawingContext context, Rect host, JetStreamField field,
            IEnumerable<GeoLocation> places, GeoLocation marker, bool compact = false)
        {
            if (field == null || field.Points.Count == 0) return;

            Rect map = MapBounds(host, field, compact);
            if (map.Width < 30 || map.Height < 30) return;

            using (context.PushClip(map))
            {
                DrawSpeedField(context, map, field);
            }

            DrawGraticule(context, map, field, compact);
            DrawWindArrows(context, map, field, compact);
            DrawPlaces(context, map, field, places, marker, compact);
            DrawJetCoreCallout(context, map, field);
            DrawLegend(context, host, compact);
        }

        /// <summary>
        /// Fills the map with the interpolated wind-speed field, drawn as small
        /// blocks with bilinear interpolation between grid points. At six pixels a
        /// block the banding is invisible at normal sizes and the repaint stays fast.
        /// </summary>
        private static void DrawSpeedField(DrawingContext context, Rect map, JetStreamField field)
        {
            const double Block = 6;

            for (double y = map.Y; y < map.Bottom; y += Block)
            {
                for (double x = map.X; x < map.Right; x += Block)
                {
                    double column = (x - map.X) / map.Width * (field.Columns - 1);
                    double row = (y - map.Y) / map.Height * (field.Rows - 1);

                    double speed = InterpolateSpeed(field, column, row);
                    if (speed < 30d) continue;   // below this there is no jet to show

                    // Opacity ramps with speed so the core stands out from the flow
                    // around it instead of the whole map reading as solid colour.
                    byte alpha = (byte)Math.Min(210d, 40d + (speed - 30d) * 2.2d);

                    context.DrawRectangle(AppTheme.Brush(AppTheme.JetSpeedColor(speed), alpha), null,
                        new Rect(x, y, Math.Min(Block, map.Right - x), Math.Min(Block, map.Bottom - y)));
                }
            }
        }

        /// <summary>Bilinear sample of the speed grid at fractional grid coordinates.</summary>
        private static double InterpolateSpeed(JetStreamField field, double column, double row)
        {
            int c0 = Math.Max(0, Math.Min((int)Math.Floor(column), field.Columns - 1));
            int r0 = Math.Max(0, Math.Min((int)Math.Floor(row), field.Rows - 1));
            int c1 = Math.Min(c0 + 1, field.Columns - 1);
            int r1 = Math.Min(r0 + 1, field.Rows - 1);

            double fc = column - c0;
            double fr = row - r0;

            double topLeft = SpeedAt(field, c0, r0);
            double topRight = SpeedAt(field, c1, r0);
            double bottomLeft = SpeedAt(field, c0, r1);
            double bottomRight = SpeedAt(field, c1, r1);

            double top = topLeft + (topRight - topLeft) * fc;
            double bottom = bottomLeft + (bottomRight - bottomLeft) * fc;
            return top + (bottom - top) * fr;
        }

        private static double SpeedAt(JetStreamField field, int column, int row)
        {
            JetStreamPoint point = field.At(column, row);
            return point == null ? 0d : point.SpeedKnots;
        }

        private static void DrawGraticule(DrawingContext context, Rect map, JetStreamField field, bool compact)
        {
            var pen = new Pen(AppTheme.Brush(AppTheme.TextColor, 46), 1,
                new DashStyle(new double[] { 1, 3 }, 0));

            for (double latitude = Math.Ceiling(field.SouthLatitude / 10d) * 10d;
                 latitude <= field.NorthLatitude; latitude += 10d)
            {
                double y = LatitudeToY(field, latitude, map);
                context.DrawLine(pen, new Point(map.X, y), new Point(map.Right, y));

                if (compact) continue;
                AppTheme.DrawLineText(context, latitude.ToString("0") + "°N", AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(map.X + 4, y - 16, 52, 15));
            }

            for (double longitude = Math.Ceiling(field.WestLongitude / 10d) * 10d;
                 longitude <= field.EastLongitude; longitude += 10d)
            {
                double x = LongitudeToX(field, longitude, map);
                context.DrawLine(pen, new Point(x, map.Y), new Point(x, map.Bottom));

                if (compact) continue;
                AppTheme.DrawLineText(context, Math.Abs(longitude).ToString("0") + "°W", AppTheme.Regular,
                    AppTheme.SizeSmall, AppTheme.TextFaint, new Rect(x + 4, map.Bottom - 18, 54, 15));
            }

            context.DrawRectangle(null, AppTheme.BorderPen, map);
        }

        /// <summary>
        /// One arrow per grid point, pointing the way the wind is blowing.
        /// Meteorological direction is the bearing the wind comes *from*, so the
        /// arrow is drawn along direction + 180 degrees.
        /// </summary>
        private static void DrawWindArrows(DrawingContext context, Rect map, JetStreamField field, bool compact)
        {
            // A phone screen cannot carry an arrow at every grid point, so on
            // compact layouts every other one is dropped in both axes.
            int step = compact ? 2 : 1;

            for (int row = 0; row < field.Rows; row += step)
            {
                for (int column = 0; column < field.Columns; column += step)
                {
                    JetStreamPoint point = field.At(column, row);
                    if (point == null || point.SpeedKnots < 25d) continue;

                    double x = LongitudeToX(field, point.Longitude, map);
                    double y = LatitudeToY(field, point.Latitude, map);
                    if (!map.Contains(new Point(x, y))) continue;

                    double heading = (point.DirectionDegrees + 180d) * Math.PI / 180d;

                    // Length carries speed as well as the colour does, which helps
                    // when the map is printed or read by someone colour-blind.
                    double length = Math.Min(26d, 8d + point.SpeedKnots / 7d);
                    double dx = Math.Sin(heading) * length;
                    double dy = -Math.Cos(heading) * length;

                    var from = new Point(x - dx / 2, y - dy / 2);
                    var to = new Point(x + dx / 2, y + dy / 2);

                    var pen = new Pen(
                        point.IsJetCore ? AppTheme.White : AppTheme.Brush(AppTheme.TextColor, 200),
                        point.IsJetCore ? 2 : 1.3,
                        lineCap: PenLineCap.Round);

                    context.DrawLine(pen, from, to);
                    DrawArrowHead(context, pen, from, to);
                }
            }
        }

        /// <summary>
        /// Avalonia pens have no arrow caps, so the head is two short strokes back
        /// along the shaft at plus and minus 25 degrees.
        /// </summary>
        private static void DrawArrowHead(DrawingContext context, IPen pen, Point from, Point to)
        {
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            const double HeadLength = 5.5;
            const double Spread = 25 * Math.PI / 180;

            context.DrawLine(pen, to, new Point(
                to.X - HeadLength * Math.Cos(angle - Spread),
                to.Y - HeadLength * Math.Sin(angle - Spread)));

            context.DrawLine(pen, to, new Point(
                to.X - HeadLength * Math.Cos(angle + Spread),
                to.Y - HeadLength * Math.Sin(angle + Spread)));
        }

        /// <summary>Marks known places so the map has geographic anchors.</summary>
        private static void DrawPlaces(DrawingContext context, Rect map, JetStreamField field,
            IEnumerable<GeoLocation> places, GeoLocation marker, bool compact)
        {
            var all = new List<GeoLocation>();
            if (places != null) all.AddRange(places);
            if (marker != null) all.Add(marker);

            foreach (GeoLocation place in all)
            {
                if (place.Latitude < field.SouthLatitude || place.Latitude > field.NorthLatitude) continue;
                if (place.Longitude < field.WestLongitude || place.Longitude > field.EastLongitude) continue;

                double x = LongitudeToX(field, place.Longitude, map);
                double y = LatitudeToY(field, place.Latitude, map);

                bool isSelected = marker != null
                                  && Math.Abs(place.Latitude - marker.Latitude) < 0.001
                                  && Math.Abs(place.Longitude - marker.Longitude) < 0.001;

                double radius = isSelected ? 5 : 3;

                context.DrawEllipse(
                    isSelected ? AppTheme.Accent : AppTheme.Brush(Colors.White, 190),
                    new Pen(AppTheme.Brush(Colors.Black, 190)),
                    new Point(x, y), radius, radius);

                bool label = isSelected || (!compact && all.Count <= 12);
                if (!label) continue;

                AppTheme.DrawLineText(context, place.Name,
                    isSelected ? AppTheme.Bold : AppTheme.Regular, AppTheme.SizeSmall,
                    isSelected ? AppTheme.Accent : AppTheme.Brush(Colors.White, 210),
                    new Rect(x + 8, y - 9, 120, 18));
            }
        }

        /// <summary>Calls out where the fastest wind in the field is.</summary>
        private static void DrawJetCoreCallout(DrawingContext context, Rect map, JetStreamField field)
        {
            JetStreamPoint core = field.StrongestPoint;
            if (core == null || core.SpeedKnots < 70d) return;

            double x = LongitudeToX(field, core.Longitude, map);
            double y = LatitudeToY(field, core.Latitude, map);

            context.DrawEllipse(null, new Pen(AppTheme.White, 2), new Point(x, y), 11, 11);

            // Flip the label to the left when the callout is near the right edge,
            // which on a narrow screen is often.
            bool flip = x + 95 > map.Right;
            var label = new Rect(flip ? x - 93 : x + 15, y - 10, 78, 20);

            context.DrawRectangle(AppTheme.Brush(Colors.Black, 190), null, label, 4, 4);
            AppTheme.DrawLineText(context, Math.Round(core.SpeedKnots).ToString("0") + " kt",
                AppTheme.Bold, AppTheme.SizeSmall, AppTheme.White, label, TextAlignment.Center);
        }

        private static void DrawLegend(DrawingContext context, Rect host, bool compact)
        {
            int[] thresholds = compact
                ? new[] { 50, 90, 130, 150 }
                : new[] { 50, 70, 90, 110, 130, 150 };

            double height = compact ? CompactLegendHeight : LegendHeight;
            double y = host.Bottom - height;
            double x = host.X + (compact ? 8 : 14);

            if (!compact)
            {
                AppTheme.DrawLineText(context, "WIND SPEED", AppTheme.Regular, AppTheme.SizeSmall,
                    AppTheme.TextFaint, new Rect(x, y, 86, 18));
                x += 90;
            }

            double swatch = compact ? 16 : 20;
            double stride = compact ? 44 : 56;

            foreach (int threshold in thresholds)
            {
                context.DrawRectangle(AppTheme.Brush(AppTheme.JetSpeedColor(threshold)), null,
                    new Rect(x, y + 5, swatch, 10), 2, 2);

                AppTheme.DrawLineText(context, threshold.ToString(CultureInfo.InvariantCulture),
                    AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextMuted,
                    new Rect(x + swatch + 4, y, 30, 18));
                x += stride;
            }

            AppTheme.DrawLineText(context, compact ? "kt" : "kt  ·  jet core is 70 kt and above",
                AppTheme.Regular, AppTheme.SizeSmall, AppTheme.TextFaint,
                new Rect(x, y, compact ? 30 : 240, 18));
        }

        // ---- projection ------------------------------------------------------

        /// <summary>
        /// The largest rectangle inside the host that keeps the box's aspect ratio,
        /// with longitude compressed by cos(centre latitude) so the region is not
        /// stretched sideways. That matters most for Alaska, where a degree of
        /// longitude is under a third of a degree of latitude.
        /// </summary>
        public static Rect MapBounds(Rect host, JetStreamField field, bool compact = false)
        {
            double legend = compact ? CompactLegendHeight : LegendHeight;
            double inset = compact ? 8 : 14;

            Rect area = host.Deflate(new Thickness(inset, inset, inset, legend + 6));
            if (area.Width <= 0 || area.Height <= 0) return default(Rect);

            double centreLatitude = (field.NorthLatitude + field.SouthLatitude) / 2d;
            double spanLatitude = Math.Max(0.1, field.NorthLatitude - field.SouthLatitude);
            double spanLongitude = Math.Max(0.1, field.EastLongitude - field.WestLongitude)
                                   * Math.Cos(centreLatitude * Math.PI / 180d);

            double aspect = spanLongitude / spanLatitude;

            double width = area.Width;
            double height = width / aspect;

            if (height > area.Height)
            {
                height = area.Height;
                width = height * aspect;
            }

            return new Rect(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                Math.Max(1, width), Math.Max(1, height));
        }

        private static double LongitudeToX(JetStreamField field, double longitude, Rect map)
        {
            double fraction = (longitude - field.WestLongitude)
                              / (field.EastLongitude - field.WestLongitude);
            return map.X + fraction * map.Width;
        }

        private static double LatitudeToY(JetStreamField field, double latitude, Rect map)
        {
            double fraction = (field.NorthLatitude - latitude)
                              / (field.NorthLatitude - field.SouthLatitude);
            return map.Y + fraction * map.Height;
        }
    }
}
