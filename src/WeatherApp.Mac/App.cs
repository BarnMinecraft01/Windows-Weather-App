using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using WeatherApp.Configuration;
using WeatherApp.Net;
using WeatherApp.Platform;
using WeatherApp.UI;

namespace WeatherApp
{
    /// <summary>
    /// Application bootstrap.
    ///
    /// Built in C# rather than XAML deliberately: almost every surface in this app
    /// is custom-drawn, so the XAML would amount to a handful of control
    /// declarations while adding a whole compile step that can fail on its own.
    /// The Fluent theme is still loaded, because the few stock controls used --
    /// combo boxes, buttons, text boxes -- need one to look like anything.
    /// </summary>
    public sealed class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());

            // The panels paint a fixed dark palette, so the stock controls are
            // pinned to the dark variant rather than following the system setting
            // and clashing with everything drawn around them.
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                AppSettings settings = AppSettings.Load();
                ApplyEndpointOverrides();
                ApplyUserAgent(settings);

                desktop.MainWindow = new MainWindow(settings);
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Loads endpoints.json if the user has one, and writes a template on first
        /// run so there is something to edit if a NOAA product path ever moves.
        /// </summary>
        private static void ApplyEndpointOverrides()
        {
            if (!AppPaths.EnsureDirectory()) return;

            try
            {
                if (File.Exists(AppSettings.EndpointsPath))
                {
                    Endpoints.LoadOverrides(AppSettings.EndpointsPath);
                }
                else
                {
                    Endpoints.WriteTemplate(AppSettings.EndpointsPath);
                }
            }
            catch (IOException)
            {
                // Defaults are compiled in; a missing config file is not fatal.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ApplyUserAgent(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ContactForUserAgent)) return;

            HttpService.UserAgent = "WindowsWeatherApp/1.0 ("
                + settings.ContactForUserAgent.Trim() + "; "
                + "+https://github.com/BarnMinecraft01/Windows-Weather-App)";
        }
    }
}
