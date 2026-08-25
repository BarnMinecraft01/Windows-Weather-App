using System;
using Avalonia;

namespace WeatherApp
{
    internal static class Program
    {
        /// <summary>
        /// Avalonia entry point. Must stay free of any Avalonia call before
        /// BuildAvaloniaApp runs, which is why configuration happens in App instead.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
