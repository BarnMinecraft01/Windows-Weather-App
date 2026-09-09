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
        private static bool _installed;
        private static bool _bootRotated;

        /// <summary>
        /// The record of the previous launch, if that launch never reported
        /// reaching a usable screen. Null when the last launch finished starting
        /// up, or when this is the first launch after an install.
        /// </summary>
        public static string PreviousBootFailure { get; private set; }

        public static void Install(Context context)
        {
            // Called from both the Application object and the launcher activity,
            // because either one may be the first thing that actually runs: if the
            // [Application] attribute failed to reach the merged manifest, the
            // Application object is Android's own and never installs anything.
            if (_installed)
            {
                _context = _context ?? context;
                return;
            }

            _installed = true;
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

        /// <summary>
        /// Opens a launch record, rotating away any record left behind by a launch
        /// that never finished starting.
        ///
        /// This is how a crash that happens before -- or instead of -- Avalonia
        /// starting is caught at all. The file is written on the way in and deleted
        /// once a screen is actually on display, so a file that is still present at
        /// the start of the next launch is proof the last one died, whether or not
        /// any managed exception handler ever got to see it.
        ///
        /// Idempotent per process: whichever of the Application object or the
        /// launcher activity runs first opens the record, and the other finds it
        /// already open.
        /// </summary>
        public static void BeginBoot()
        {
            if (_bootRotated) return;
            _bootRotated = true;

            try
            {
                string path = BootStatePath();
                if (path == null) return;

                if (File.Exists(path))
                {
                    string previous = File.ReadAllText(path);
                    if (!previous.Contains(BootCompleteMarker))
                    {
                        PreviousBootFailure = previous;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, BootHeader());
            }
            catch (Exception)
            {
                // A missing breadcrumb trail must never itself be the crash.
            }
        }

        /// <summary>Records a step of startup into the current launch record.</summary>
        public static void Breadcrumb(string step)
        {
            try
            {
                string path = BootStatePath();
                if (path == null) return;

                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + step + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Marks the launch as having reached a usable screen, so the next launch
        /// starts normally instead of reporting this one as a failure.
        /// </summary>
        public static void CompleteBoot()
        {
            Breadcrumb(BootCompleteMarker);
        }

        /// <summary>Forgets a reported failure so it is only shown once.</summary>
        public static void ClearPreviousBootFailure()
        {
            PreviousBootFailure = null;
        }

        private const string BootCompleteMarker = "BOOT-COMPLETE";

        private static string BootStatePath()
        {
            string directory = ExternalDirectory();
            return directory == null ? null : Path.Combine(directory, "boot.log");
        }

        private static string BootHeader()
        {
            var builder = new StringBuilder();
            builder.Append("Launch at ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.Append("Device: ")
                   .Append(Android.OS.Build.Manufacturer).Append(' ').Append(Android.OS.Build.Model)
                   .Append(" / Android ").Append(Android.OS.Build.VERSION.Release)
                   .Append(" (API ").Append((int)Android.OS.Build.VERSION.SdkInt).Append(')')
                   .AppendLine();
            builder.Append("ABIs: ").AppendLine(string.Join(", ", Android.OS.Build.SupportedAbis ?? new string[0]));
            builder.AppendLine();
            return builder.ToString();
        }

        /// <summary>The crash log from earlier launches, for showing alongside the trail.</summary>
        public static string ReadCrashLog()
        {
            try
            {
                string path = CrashLogPath;
                return File.Exists(path) ? File.ReadAllText(path) : null;
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
