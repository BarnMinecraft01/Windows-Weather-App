using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace WeatherApp.Droid
{
    /// <summary>
    /// The Android Application object that starts Avalonia.
    ///
    /// Avalonia 12 initialises here rather than in the activity, so that every
    /// activity the platform creates shares one Avalonia application instance.
    /// The two-argument constructor is required by Android: the runtime
    /// resurrects this type from a Java handle rather than calling a parameterless
    /// constructor.
    /// </summary>
    [Application(
        Label = "Windows Weather",
        Icon = "@drawable/icon",
        Theme = "@style/AppTheme")]
    public class AndroidApp : AvaloniaAndroidApplication<App>
    {
        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            // Installed before anything else runs, so that a failure during Avalonia
            // startup still leaves a readable reason behind rather than the app
            // silently returning to the launcher.
            CrashReporter.Install(this);
            CrashReporter.BeginBoot();
            CrashReporter.Breadcrumb("AndroidApp.OnCreate entered");

            try
            {
                base.OnCreate();
                CrashReporter.Breadcrumb("Avalonia initialised");
            }
            catch (Exception ex)
            {
                // Letting this escape kills the process before any activity runs,
                // which is the failure that shows as the app opening and closing
                // with nothing on screen. Recording it and returning lets the
                // launcher activity start and show what happened -- an app that
                // can explain itself beats one that vanishes.
                CrashReporter.Report("Avalonia initialisation", ex);
                CrashReporter.Breadcrumb("Avalonia initialisation FAILED: " + ex.GetType().Name);
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder);
        }
    }
}
