using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WeatherApp.Net
{
    /// <summary>
    /// A short record of what the app asked the weather services for and what came
    /// back.
    ///
    /// This exists because of how the first working build failed. Four upstreams,
    /// two of them broken, and the only thing on screen was one truncated sentence
    /// -- enough to know something was wrong and not enough to know what. Nobody
    /// testing this has a debugger attached to their phone, so the app has to be
    /// able to say for itself which host it called, what it got, and what the
    /// exception actually said.
    ///
    /// Deliberately small and in memory only: it is a window on the current
    /// session, not a log file to manage.
    /// </summary>
    public static class RequestLog
    {
        public sealed class Entry
        {
            public DateTime WhenUtc;
            public string Host;
            public string Url;
            public string Outcome;
            public string Error;
        }

        private const int Capacity = 60;

        private static readonly object Lock = new object();
        private static readonly Queue<Entry> Entries = new Queue<Entry>();

        public static void Record(string host, string url, string outcome, Exception error)
        {
            var entry = new Entry
            {
                WhenUtc = DateTime.UtcNow,
                Host = host,
                Url = url,
                Outcome = outcome,
                Error = Describe(error)
            };

            lock (Lock)
            {
                Entries.Enqueue(entry);
                while (Entries.Count > Capacity) Entries.Dequeue();
            }
        }

        public static void Clear()
        {
            lock (Lock) { Entries.Clear(); }
        }

        public static List<Entry> Snapshot()
        {
            lock (Lock) { return new List<Entry>(Entries); }
        }

        /// <summary>
        /// The whole session as text, for showing on screen or sharing.
        ///
        /// Query strings are kept. They carry the coordinates and the field lists,
        /// which is exactly where a wrong request shows itself; these are public
        /// weather endpoints with no keys or credentials in them.
        /// </summary>
        public static string Report(IEnumerable<string> warnings)
        {
            var builder = new StringBuilder();
            builder.AppendLine("=== Windows Weather diagnostics ===");
            builder.Append("When: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();

            if (warnings != null)
            {
                var listed = new List<string>(warnings);
                builder.Append("Warnings (").Append(listed.Count).AppendLine("):");

                if (listed.Count == 0)
                {
                    builder.AppendLine("  (none)");
                }
                else
                {
                    // Numbered and unabbreviated: the status bar can only show the
                    // first one and cuts it off, which is the gap this closes.
                    for (int i = 0; i < listed.Count; i++)
                    {
                        builder.Append("  ").Append(i + 1).Append(". ").AppendLine(listed[i]);
                    }
                }
                builder.AppendLine();
            }

            List<Entry> entries = Snapshot();
            builder.Append("Requests (").Append(entries.Count).AppendLine("):");

            if (entries.Count == 0)
            {
                builder.AppendLine("  (none yet)");
                return builder.ToString();
            }

            foreach (Entry entry in entries)
            {
                builder.Append("  ")
                       .Append(entry.WhenUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                       .Append("  ")
                       .Append(entry.Host)
                       .Append("  ")
                       .AppendLine(entry.Outcome);

                builder.Append("      ").AppendLine(entry.Url);

                if (!string.IsNullOrEmpty(entry.Error))
                {
                    builder.Append("      ").AppendLine(entry.Error);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// An exception as one line per nested cause. The type name matters as much
        /// as the message: "InvalidDataException" says decompression where the
        /// message alone might not.
        /// </summary>
        private static string Describe(Exception error)
        {
            if (error == null) return null;

            var builder = new StringBuilder();
            for (Exception current = error; current != null; current = current.InnerException)
            {
                if (builder.Length > 0) builder.Append(" <- ");
                builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return builder.ToString();
        }
    }
}
