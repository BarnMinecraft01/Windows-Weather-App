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

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder);
        }
    }
}
