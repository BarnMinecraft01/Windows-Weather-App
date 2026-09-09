using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using WeatherApp.Json;

namespace WeatherApp.Net
{
    /// <summary>Raised when an upstream service could not be reached or understood.</summary>
    public class WeatherServiceException : Exception
    {
        public WeatherServiceException(string message) : base(message) { }
        public WeatherServiceException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// The single outbound HTTP path for the whole app.
    ///
    /// Three behaviours here exist specifically for the older Windows versions this
    /// app supports and for the fact that every upstream is a free public service:
    ///
    ///  * TLS 1.2 is turned on explicitly. Windows 7 and 8 negotiate TLS 1.0 by
    ///    default, and every endpoint the app uses refuses that.
    ///  * Responses are cached in memory with a per-request TTL, so switching tabs
    ///    does not re-hit api.weather.gov.
    ///  * On a network failure an expired cache entry is served rather than thrown
    ///    away ("stale-if-error"). A forecast from twenty minutes ago beats an
    ///    empty window when the connection drops.
    /// </summary>
    public static class HttpService
    {
        private sealed class CacheEntry
        {
            public byte[] Payload;
            public DateTime FetchedUtc;
            public DateTime ExpiresUtc;
        }

        private const int MaxCacheEntries = 240;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient Client;

        /// <summary>
        /// A second client that asks for no compression at all, used to retry a host
        /// whose compressed response this platform could not inflate.
        /// </summary>
        private static readonly HttpClient PlainClient;

        /// <summary>
        /// Hosts that have already failed to decompress once. Requests to them skip
        /// straight to the uncompressed client rather than failing again first.
        /// </summary>
        private static readonly HashSet<string> PlainHosts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Sent on every request. The NWS API asks callers to identify themselves and
        /// throttles anonymous-looking traffic, so this is not merely decorative.
        /// </summary>
        public static string UserAgent = "WindowsWeatherApp/1.0 (+https://github.com/BarnMinecraft01/Windows-Weather-App)";

        static HttpService()
        {
            EnableModernTls();

            var handler = new HttpClientHandler();
            try
            {
                // gzip only, deliberately. "deflate" is ambiguous on the wire -- some
                // servers send it wrapped in a zlib header and some raw -- and which
                // of the two a given platform's inflater expects varies. Asking only
                // for gzip removes the ambiguity, and it is the encoding every one of
                // these services supports anyway.
                handler.AutomaticDecompression = DecompressionMethods.GZip;
            }
            catch (NotSupportedException)
            {
                // Very old platform builds may refuse; uncompressed responses still work.
            }
            handler.AllowAutoRedirect = true;
            handler.UseProxy = true;

            Client = new HttpClient(handler);
            Client.Timeout = TimeSpan.FromSeconds(30);

            var plainHandler = new HttpClientHandler();
            try
            {
                plainHandler.AutomaticDecompression = DecompressionMethods.None;
            }
            catch (NotSupportedException)
            {
            }
            plainHandler.AllowAutoRedirect = true;
            plainHandler.UseProxy = true;

            PlainClient = new HttpClient(plainHandler);
            PlainClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Adds TLS 1.2 (and 1.3 where the OS knows it) to the protocol list.
        ///
        /// This exists purely for the .NET Framework head. Windows 7 and 8 negotiate
        /// TLS 1.0 by default and every endpoint here refuses that. On .NET 6 and
        /// later the whole ServicePointManager surface is obsolete and ignored by
        /// SocketsHttpHandler, so the block is compiled out rather than left to
        /// emit obsolescence warnings and do nothing.
        ///
        /// The numeric literals avoid a compile-time dependency on enum members that
        /// do not exist in every .NET Framework service release, and each assignment
        /// is guarded because Windows 7 without the relevant update throws on the
        /// TLS 1.3 value rather than ignoring it.
        /// </summary>
        private static void EnableModernTls()
        {
#if NETFRAMEWORK
            const int Tls12 = 3072;
            const int Tls13 = 12288;

            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)Tls12;
            }
            catch (NotSupportedException)
            {
                // Nothing else to try; the requests below will surface a clear error.
            }

            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)Tls13;
            }
            catch (NotSupportedException)
            {
                // Expected on Windows 7/8/8.1. TLS 1.2 alone is sufficient.
            }

            ServicePointManager.DefaultConnectionLimit = 12;
            ServicePointManager.Expect100Continue = false;
#endif
        }

        public static void ClearCache()
        {
            lock (CacheLock) { Cache.Clear(); }
        }

        /// <summary>Fetches a URL and parses it as JSON.</summary>
        public static async Task<JsonValue> GetJsonAsync(
            string url, string accept, TimeSpan ttl, CancellationToken cancellationToken)
        {
            string body = await GetStringAsync(url, accept, ttl, cancellationToken).ConfigureAwait(false);

            JsonValue parsed;
            if (!JsonValue.TryParse(body, out parsed))
            {
                throw new WeatherServiceException(
                    "The response from " + DescribeHost(url) + " was not valid JSON.");
            }
            return parsed;
        }

        public static async Task<string> GetStringAsync(
            string url, string accept, TimeSpan ttl, CancellationToken cancellationToken)
        {
            byte[] payload = await GetBytesAsync(url, accept, ttl, cancellationToken).ConfigureAwait(false);
            return DecodeUtf8(payload);
        }

        /// <summary>
        /// Core fetch. Returns cached bytes while they are fresh, otherwise goes to the
        /// network, and falls back to stale bytes if the network attempt fails.
        /// </summary>
        public static async Task<byte[]> GetBytesAsync(
            string url, string accept, TimeSpan ttl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("A URL is required.", "url");

            CacheEntry cached = ReadCache(url);
            if (cached != null && DateTime.UtcNow < cached.ExpiresUtc)
            {
                return cached.Payload;
            }

            try
            {
                byte[] fetched = await FetchWithRetryAsync(url, accept, cancellationToken).ConfigureAwait(false);
                WriteCache(url, fetched, ttl);
                return fetched;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Recorded before the stale-cache fallback hides it: a request that
                // keeps failing while an old copy carries the screen is exactly the
                // failure nobody would otherwise notice.
                RequestLog.Record(DescribeHost(url), url,
                    cached != null ? "failed, served a stale copy" : "failed", ex);

                // stale-if-error: an old reading is more useful than a blank panel.
                if (cached != null) return cached.Payload;

                throw new WeatherServiceException(
                    "Could not reach " + DescribeHost(url) + ". " + Summarise(ex), ex);
            }
        }

        private static async Task<byte[]> FetchWithRetryAsync(
            string url, string accept, CancellationToken cancellationToken)
        {
            const int MaxAttempts = 3;
            Exception last = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await FetchOnceAsync(url, accept, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (WeatherServiceException ex)
                {
                    // A 404 or 400 will not fix itself; stop immediately.
                    if (!ex.Data.Contains("transient")) throw;
                    last = ex;
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                if (attempt < MaxAttempts)
                {
                    int delayMs = 400 * (int)Math.Pow(2, attempt - 1); // 400ms, 800ms
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }

            throw last ?? new WeatherServiceException("Request to " + DescribeHost(url) + " failed.");
        }

        /// <summary>
        /// One request, retried without compression if the compressed reply could not
        /// be inflated.
        ///
        /// That retry is not theoretical: on Android, api.open-meteo.com and
        /// geocoding-api.open-meteo.com both failed with "the archive entry was
        /// compressed using an unsupported compression method" while api.weather.gov
        /// over the same connection was fine. The bytes arrive; the platform simply
        /// cannot expand them. Asking that host for plain text afterwards costs some
        /// bandwidth and always works, which beats showing an empty forecast.
        /// </summary>
        private static async Task<byte[]> FetchOnceAsync(
            string url, string accept, CancellationToken cancellationToken)
        {
            string host = DescribeHost(url);

            if (PrefersPlain(host))
            {
                return await SendAsync(PlainClient, url, accept, true, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await SendAsync(Client, url, accept, false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsDecompressionFailure(ex))
            {
                // Remembered, so the rest of this session's requests to the same host
                // do not each pay for the same discovery.
                RememberPlainHost(host);
                RequestLog.Record(host, url, "compressed reply could not be inflated; retrying uncompressed", ex);

                return await SendAsync(PlainClient, url, accept, true, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> SendAsync(
            HttpClient client, string url, string accept, bool plain, CancellationToken cancellationToken)
        {
            var started = DateTime.UtcNow;

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                if (!string.IsNullOrEmpty(accept))
                {
                    request.Headers.TryAddWithoutValidation("Accept", accept);
                }

                // Stated outright on the plain client. Without it a server is free to
                // compress anyway, which is the thing being avoided.
                if (plain) request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using (HttpResponseMessage response = await client
                           .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                           .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        var failure = new WeatherServiceException(
                            DescribeHost(url) + " returned HTTP " + code.ToString(CultureInfo.InvariantCulture)
                            + " (" + response.ReasonPhrase + ").");

                        // 429 and 5xx are worth another attempt; 4xx generally is not.
                        if (code == 429 || code >= 500) failure.Data["transient"] = true;

                        RequestLog.Record(DescribeHost(url), url,
                            "HTTP " + code.ToString(CultureInfo.InvariantCulture), null);
                        throw failure;
                    }

                    byte[] payload = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                    RequestLog.Record(DescribeHost(url), url,
                        "HTTP 200, " + payload.Length.ToString(CultureInfo.InvariantCulture) + " bytes"
                        + (plain ? ", uncompressed" : string.Empty)
                        + ", " + ((int)(DateTime.UtcNow - started).TotalMilliseconds)
                            .ToString(CultureInfo.InvariantCulture) + " ms",
                        null);

                    return payload;
                }
            }
        }

        /// <summary>
        /// Whether an exception is the response body failing to decompress rather than
        /// the request failing to arrive.
        ///
        /// Matched on both type and message because the platforms disagree about which
        /// exception carries it: it surfaces as InvalidDataException on some, wrapped in
        /// an IOException or HttpRequestException on others.
        /// </summary>
        private static bool IsDecompressionFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is InvalidDataException) return true;

                string message = current.Message;
                if (string.IsNullOrEmpty(message)) continue;

                if (message.IndexOf("compress", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (message.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (message.IndexOf("deflate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (message.IndexOf("archive entry", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private static bool PrefersPlain(string host)
        {
            lock (CacheLock) { return PlainHosts.Contains(host); }
        }

        private static void RememberPlainHost(string host)
        {
            lock (CacheLock) { PlainHosts.Add(host); }
        }

        private static CacheEntry ReadCache(string url)
        {
            lock (CacheLock)
            {
                CacheEntry entry;
                return Cache.TryGetValue(url, out entry) ? entry : null;
            }
        }

        private static void WriteCache(string url, byte[] payload, TimeSpan ttl)
        {
            if (payload == null) return;

            lock (CacheLock)
            {
                if (Cache.Count >= MaxCacheEntries && !Cache.ContainsKey(url))
                {
                    EvictOldest();
                }

                DateTime now = DateTime.UtcNow;
                Cache[url] = new CacheEntry
                {
                    Payload = payload,
                    FetchedUtc = now,
                    ExpiresUtc = now.Add(ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : ttl)
                };
            }
        }

        /// <summary>Drops the least recently fetched quarter of the cache. Caller holds the lock.</summary>
        private static void EvictOldest()
        {
            var ordered = new List<KeyValuePair<string, CacheEntry>>(Cache);
            ordered.Sort((a, b) => a.Value.FetchedUtc.CompareTo(b.Value.FetchedUtc));

            int removeCount = Math.Max(1, ordered.Count / 4);
            for (int i = 0; i < removeCount; i++)
            {
                Cache.Remove(ordered[i].Key);
            }
        }

        private static string DecodeUtf8(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return string.Empty;

            // Strip a UTF-8 BOM if the server sent one; it would otherwise become a
            // leading character that trips up the JSON reader.
            int offset = 0;
            if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
            {
                offset = 3;
            }
            return System.Text.Encoding.UTF8.GetString(payload, offset, payload.Length - offset);
        }

        private static string DescribeHost(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch (UriFormatException)
            {
                return "the weather service";
            }
        }

        private static string Summarise(Exception ex)
        {
            var web = ex as WebException;
            if (web != null && web.Status == WebExceptionStatus.NameResolutionFailure)
            {
                return "The address could not be resolved -- check the internet connection.";
            }

            var io = ex as IOException;
            if (io != null)
            {
                return "The connection was interrupted.";
            }

            if (ex is TaskCanceledException)
            {
                return "The request timed out.";
            }

            return ex.Message;
        }
    }
}
