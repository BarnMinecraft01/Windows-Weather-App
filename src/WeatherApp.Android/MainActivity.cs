using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace WeatherApp.Droid
{
    /// <summary>
    /// The activity that hosts Avalonia.
    ///
    /// Non-generic: Avalonia 12 moved application initialisation out of the
    /// activity and into an Android Application subclass (see AndroidApp), so
    /// the activity no longer names the Avalonia app type.
    ///
    /// ConfigurationChanges is deliberately broad. Without it Android destroys and
    /// recreates the activity on rotation or a theme change, which would throw
    /// away the loaded snapshot and re-hit the weather services for no reason.
    /// Avalonia handles the resize itself.
    ///
    /// Not the launcher activity: LauncherActivity runs first and decides whether
    /// starting Avalonia is worth attempting, because this class cannot make that
    /// decision without starting it first.
    /// </summary>
    [Activity(
        Label = "Windows Weather",
        Theme = "@style/AppTheme",
        Exported = false,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation
                               | ConfigChanges.ScreenSize
                               | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize
                               | ConfigChanges.UiMode
                               | ConfigChanges.Density)]
    public class MainActivity : AvaloniaMainActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            CrashReporter.Breadcrumb("MainActivity.OnCreate entered");

            try
            {
                base.OnCreate(savedInstanceState);
            }
            catch (Exception ex)
            {
                // Rethrown deliberately: the base call may not have completed, and
                // Android tears down an activity that did not call through to
                // super. Recording it first means the next launch can explain it.
                CrashReporter.Report("MainActivity.OnCreate", ex);
                CrashReporter.Breadcrumb("MainActivity.OnCreate FAILED: " + ex.GetType().Name);
                throw;
            }

            CrashReporter.Breadcrumb("MainActivity.OnCreate returned");
        }
    }
}
