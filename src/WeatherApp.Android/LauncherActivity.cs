using System;
using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace WeatherApp.Droid
{
    /// <summary>
    /// The activity Android actually launches, which decides whether to start the
    /// app or to explain why the last attempt failed.
    ///
    /// This indirection exists because MainActivity cannot make that decision:
    /// it derives from AvaloniaMainActivity, and an Android activity must call
    /// base.OnCreate or the platform throws SuperNotCalledException -- so by the
    /// time MainActivity could inspect anything, Avalonia has already started,
    /// which is the very thing suspected of crashing. A plain Activity in front
    /// of it has no such constraint, and needs neither Avalonia nor the app's own
    /// Application object to have survived.
    /// </summary>
    [Activity(
        Label = "Windows Weather",
        Icon = "@drawable/icon",
        Theme = "@style/AppTheme",
        MainLauncher = true,
        NoHistory = true,
        Exported = true,
        LaunchMode = LaunchMode.SingleTop)]
    public class LauncherActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string diagnosis = null;

            try
            {
                // Both of these are no-ops if AndroidApp already ran them. If it
                // did not -- which is itself one of the things being tested -- this
                // is the first chance anything gets to record what happened.
                CrashReporter.Install(ApplicationContext);
                CrashReporter.BeginBoot();
                CrashReporter.Breadcrumb("LauncherActivity.OnCreate");

                diagnosis = Diagnose();
            }
            catch (Exception ex)
            {
                diagnosis = "The launcher's own checks failed: " + ex;
            }

            if (diagnosis != null)
            {
                CrashReporter.ClearPreviousBootFailure();
                ShowReport(diagnosis);
                return;
            }

            StartApp();
        }

        /// <summary>
        /// Returns a report if the app should not simply be started, or null if
        /// there is nothing to say and the app should launch normally.
        /// </summary>
        private string Diagnose()
        {
            // Asked two ways because a context can be a wrapper rather than the
            // Application object itself, and a false negative here would send a
            // perfectly healthy app to the report screen.
            object application = Android.App.Application.Context ?? ApplicationContext;
            bool applicationRegistered = application is AndroidApp || ApplicationContext is AndroidApp;
            string previous = CrashReporter.PreviousBootFailure;

            // The app's Application subclass is what starts Avalonia. If Android
            // built a plain Application instead, the [Application] attribute never
            // reached the merged manifest and nothing will ever initialise --
            // every launch fails identically and silently. That is worth reporting
            // on the first launch, without waiting for a crash to be recorded.
            if (applicationRegistered && previous == null) return null;

            var builder = new StringBuilder();

            if (!applicationRegistered)
            {
                builder.AppendLine("The app's Application class was not used by Android.");
                builder.Append("Expected: ").AppendLine(typeof(AndroidApp).FullName);
                builder.Append("Actual:   ")
                       .AppendLine(application?.GetType().FullName ?? "(none)");
                builder.AppendLine();
                builder.AppendLine(
                    "Avalonia is started from that class, so nothing was initialised.");
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine("The previous launch did not reach a usable screen.");
                builder.AppendLine();
            }

            builder.Append("Version: ").AppendLine(VersionName());
            builder.AppendLine();

            if (previous != null)
            {
                builder.AppendLine("--- how far the previous launch got ---");
                builder.AppendLine(previous);
            }

            string crashLog = CrashReporter.ReadCrashLog();
            if (!string.IsNullOrEmpty(crashLog))
            {
                builder.AppendLine("--- recorded exceptions ---");
                builder.AppendLine(crashLog);
            }
            else
            {
                builder.AppendLine("No exception was recorded, which points at a failure "
                                   + "below managed code rather than a thrown exception.");
            }

            builder.AppendLine();
            builder.Append("Log file: ").AppendLine(CrashReporter.CrashLogPath);

            return builder.ToString();
        }

        private string VersionName()
        {
            try
            {
                PackageInfo info = PackageManager.GetPackageInfo(PackageName, 0);
                return info.VersionName;
            }
            catch (Exception)
            {
                return "(unknown)";
            }
        }

        private void ShowReport(string report)
        {
            var intent = new Intent(this, typeof(CrashActivity));
            intent.PutExtra(CrashActivity.ReportExtra, report);
            StartActivity(intent);
            Finish();
        }

        private void StartApp()
        {
            try
            {
                CrashReporter.Breadcrumb("starting MainActivity");
                StartActivity(new Intent(this, typeof(MainActivity)));
                Finish();
            }
            catch (Exception ex)
            {
                CrashReporter.Report("LauncherActivity", ex);
                ShowReport(CrashReporter.Format("LauncherActivity", ex));
            }
        }
    }
}
