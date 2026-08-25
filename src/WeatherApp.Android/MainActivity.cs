using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace WeatherApp.Droid
{
    /// <summary>
    /// The single activity that hosts Avalonia.
    ///
    /// ConfigurationChanges is deliberately broad: without it Android destroys and
    /// recreates the activity on rotation or a theme change, which would throw away
    /// the loaded snapshot and re-hit the weather services for no reason. Avalonia
    /// handles the resize itself.
    /// </summary>
    [Activity(
        Label = "Windows Weather",
        Theme = "@style/AppTheme",
        Icon = "@drawable/icon",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation
                               | ConfigChanges.ScreenSize
                               | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize
                               | ConfigChanges.UiMode
                               | ConfigChanges.Density)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder);
        }
    }
}
