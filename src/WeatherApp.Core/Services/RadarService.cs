using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>
    /// One downloaded radar frame, kept as the raw encoded bytes.
    ///
    /// Decoding deliberately does not happen here. System.Drawing is a
    /// Windows-only library from .NET 6 onwards -- and the escape-hatch switch
    /// that briefly re-enabled it elsewhere was removed in .NET 7 -- so a decoded
    /// image in the shared core would be the single thing stopping this code from
    /// compiling on macOS. Each UI head turns these bytes into its own native
    /// image type instead.
    /// </summary>
    public sealed class RadarFrame
    {
        /// <summary>The encoded GIF exactly as the server sent it.</summary>
        public byte[] Data { get; set; }

        /// <summary>0 is the newest frame; higher numbers are further back.</summary>
        public int Index { get; set; }
    }

    /// <summary>A radar loop: newest-last, ready to animate.</summary>
    public sealed class RadarLoop
    {
        public RadarLoop()
        {
            Frames = new List<RadarFrame>();
        }

        public List<RadarFrame> Frames { get; private set; }
        public string SectorCode { get; set; }
        public DateTime RetrievedAt { get; set; }

        /// <summary>Set when only the pre-built animated GIF could be fetched.</summary>
        public bool IsAnimatedGif { get; set; }
    }

    /// <summary>
    /// Fetches NWS RIDGE II radar imagery.
    ///
    /// The service publishes both a ready-made animated GIF and the ten individual
    /// frames behind it. The frames are preferred, because having them separately
    /// is what allows the app to offer play/pause and a scrub bar instead of an
    /// animation the user cannot stop on the frame they want to look at. If the
    /// frames cannot be fetched the animated GIF is used as a fallback so the tab
    /// still shows something.
    /// </summary>
    public sealed class RadarService
    {
        private static readonly TimeSpan FrameTtl = TimeSpan.FromMinutes(4);

        /// <summary>How many frames RIDGE II keeps for a sector.</summary>
        public const int FrameCount = 10;

        public async Task<RadarLoop> GetLoopAsync(string sectorCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sectorCode))
            {
                throw new ArgumentException("A radar sector or station is required.", "sectorCode");
            }

            var loop = new RadarLoop
            {
                SectorCode = sectorCode,
                RetrievedAt = DateTime.Now
            };

            // Walk oldest to newest so the list plays forward without reversing.
            for (int index = FrameCount - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string url = string.Format(
                    CultureInfo.InvariantCulture, Endpoints.RadarFrameFormat,
                    sectorCode, index.ToString(CultureInfo.InvariantCulture));

                try
                {
                    byte[] payload = await HttpService
                        .GetBytesAsync(url, "image/gif", FrameTtl, cancellationToken)
                        .ConfigureAwait(false);

                    if (LooksLikeImage(payload))
                    {
                        loop.Frames.Add(new RadarFrame { Data = payload, Index = index });
                    }
                }
                catch (WeatherServiceException)
                {
                    // A single missing frame is normal when a volume scan is mid-upload.
                    // Only a completely empty loop is a real failure.
                }
            }

            if (loop.Frames.Count > 0) return loop;

            await LoadAnimatedFallbackAsync(loop, sectorCode, cancellationToken).ConfigureAwait(false);
            return loop;
        }

        private static async Task LoadAnimatedFallbackAsync(
            RadarLoop loop, string sectorCode, CancellationToken cancellationToken)
        {
            string url = string.Format(CultureInfo.InvariantCulture, Endpoints.RadarLoopFormat, sectorCode);

            byte[] payload = await HttpService
                .GetBytesAsync(url, "image/gif", FrameTtl, cancellationToken)
                .ConfigureAwait(false);

            if (!LooksLikeImage(payload))
            {
                throw new WeatherServiceException(
                    "No radar imagery is available for " + sectorCode + " right now.");
            }

            loop.IsAnimatedGif = true;
            loop.Frames.Add(new RadarFrame { Data = payload, Index = 0 });
        }

        /// <summary>Link to the full interactive radar site for this sector.</summary>
        public static string BrowserUrl(string browserSlug)
        {
            return string.Format(CultureInfo.InvariantCulture, Endpoints.RadarBrowserFormat, browserSlug);
        }

        /// <summary>
        /// Cheap sniff for a real image, without decoding it.
        ///
        /// The failure this guards against is a 200 response carrying an HTML error
        /// page instead of a GIF, which the servers here do occasionally return.
        /// Checking the magic bytes catches that in the shared core, so neither UI
        /// head has to handle a decode fault mid-paint.
        /// </summary>
        internal static bool LooksLikeImage(byte[] payload)
        {
            if (payload == null || payload.Length < 8) return false;

            // "GIF87a" / "GIF89a"
            if (payload[0] == 'G' && payload[1] == 'I' && payload[2] == 'F') return true;

            // PNG signature
            if (payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4E && payload[3] == 0x47)
            {
                return true;
            }

            // JPEG start-of-image
            if (payload[0] == 0xFF && payload[1] == 0xD8) return true;

            return false;
        }
    }
}
