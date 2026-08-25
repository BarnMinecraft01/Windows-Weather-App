using Android.App;
using Android.Content.PM;
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
    /// </summary>
    [Activity(
        Label = "Windows Weather",
        Theme = "@style/AppTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation
                               | ConfigChanges.ScreenSize
                               | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize
                               | ConfigChanges.UiMode
                               | ConfigChanges.Density)]
    public class MainActivity : AvaloniaMainActivity
    {
    }
}
