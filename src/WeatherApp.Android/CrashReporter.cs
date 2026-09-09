using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Android.Runtime;

namespace WeatherApp.Droid
{
    /// <summary>
    /// Captures unhandled exceptions somewhere a person without a development
    /// machine can actually read them.
    ///
    /// An Android app that throws during startup simply disappears: the launcher
    /// returns and nothing is shown. Without adb the reason is invisible, which
    /// makes a launch crash almost impossible to report usefully. So every
    /// unhandled exception is written twice -- to logcat for anyone with a cable,
    /// and to a file under the app's external files directory, which the Files app
    /// can open with no tooling at all.
    /// </summary>
    internal static class CrashReporter
    {
        public const string Tag = "WindowsWeather";

        private static Context _context;

        public static void Install(Context context)
        {
            _context = context;

            // Managed exceptions surfacing through the Java bridge.
            AndroidEnvironment.UnhandledExceptionRaiser += (sender, e) =>
            {
                Report("AndroidEnvironment", e.Exception);

                // Deliberately not marking it handled. Swallowing it would leave the
                // process in an unknown state; the goal is that the reason survives
                // the crash, not that the crash is hidden.
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                Report("AppDomain", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Report("Task", e.Exception);
                e.SetObserved();
            };
        }

        /// <summary>Where the log is written, for showing the user.</summary>
        public static string CrashLogPath
        {
            get
            {
                string directory = ExternalDirectory();
                return directory == null ? "(unavailable)" : Path.Combine(directory, "crash.log");
            }
        }

        public static void Report(string source, Exception exception)
        {
            string text = Format(source, exception);

            try
            {
                Android.Util.Log.Error(Tag, text);
            }
            catch (Exception)
            {
                // Logging must never itself throw during crash handling.
            }

            try
            {
                string directory = ExternalDirectory();
                if (directory == null) return;

                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "crash.log"), text + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// The app's external files directory, which is world-readable through the
        /// Files app and needs no storage permission. Falls back to internal
        /// storage, which at least keeps logcat's copy company.
        /// </summary>
        private static string ExternalDirectory()
        {
            try
            {
                Java.IO.File external = _context?.GetExternalFilesDir(null);
                if (external != null) return external.AbsolutePath;

                return _context?.FilesDir?.AbsolutePath;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Format(string source, Exception exception)
        {
            var builder = new StringBuilder();
            builder.AppendLine("=== Windows Weather crash ===");
            builder.Append("When:   ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.Append("Source: ").AppendLine(source);
            builder.Append("Device: ")
                   .Append(Android.OS.Build.Manufacturer).Append(' ').Append(Android.OS.Build.Model)
                   .Append(" / Android ").Append(Android.OS.Build.VERSION.Release)
                   .Append(" (API ").Append((int)Android.OS.Build.VERSION.SdkInt).Append(')')
                   .AppendLine();
            builder.AppendLine();

            if (exception == null)
            {
                builder.AppendLine("No exception object was supplied.");
                return builder.ToString();
            }

            for (Exception current = exception; current != null; current = current.InnerException)
            {
                builder.Append(current.GetType().FullName).Append(": ").AppendLine(current.Message);
                builder.AppendLine(current.StackTrace ?? "(no stack trace)");
                if (current.InnerException != null) builder.AppendLine("--- caused by ---");
            }

            return builder.ToString();
        }
    }
}
