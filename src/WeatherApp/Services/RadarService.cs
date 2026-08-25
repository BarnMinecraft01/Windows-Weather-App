using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Configuration;
using WeatherApp.Net;

namespace WeatherApp.Services
{
    /// <summary>One downloaded radar frame.</summary>
    public sealed class RadarFrame : IDisposable
    {
        public Image Image { get; set; }

        /// <summary>0 is the newest frame; higher numbers are further back.</summary>
        public int Index { get; set; }

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }

    /// <summary>A radar loop: newest-last, ready to animate.</summary>
    public sealed class RadarLoop : IDisposable
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

        public void Dispose()
        {
            foreach (RadarFrame frame in Frames) frame.Dispose();
            Frames.Clear();
        }
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

                    Image image = DecodeImage(payload);
                    if (image != null)
                    {
                        loop.Frames.Add(new RadarFrame { Image = image, Index = index });
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

            Image image = DecodeImage(payload);
            if (image == null)
            {
                throw new WeatherServiceException(
                    "No radar imagery is available for " + sectorCode + " right now.");
            }

            loop.IsAnimatedGif = true;
            loop.Frames.Add(new RadarFrame { Image = image, Index = 0 });
        }

        /// <summary>Link to the full interactive radar site for this sector.</summary>
        public static string BrowserUrl(string browserSlug)
        {
            return string.Format(CultureInfo.InvariantCulture, Endpoints.RadarBrowserFormat, browserSlug);
        }

        /// <summary>
        /// Decodes downloaded bytes into an Image.
        ///
        /// The stream is deliberately kept alive for the lifetime of the Image:
        /// GDI+ reads from it lazily, and disposing it here would fault later during
        /// painting. The MemoryStream owns no unmanaged handles, so letting the
        /// Image hold it is safe.
        /// </summary>
        private static Image DecodeImage(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return null;

            try
            {
                var stream = new MemoryStream(payload, false);
                return Image.FromStream(stream);
            }
            catch (ArgumentException)
            {
                // Not an image -- most often an HTML error page served with a 200.
                return null;
            }
            catch (OutOfMemoryException)
            {
                // GDI+ reports an unrecognised image format this way.
                return null;
            }
        }
    }
}
