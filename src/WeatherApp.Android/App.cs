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

namespace WeatherApp.Droid
{
    /// <summary>
    /// Android application bootstrap.
    ///
    /// The only structural difference from the desktop head is the lifetime:
    /// Android is a single-view platform, so the shell is assigned to MainView
    /// rather than to a MainWindow. Everything below the UI -- settings,
    /// endpoint overrides, the user agent -- is the same shared code.
    /// </summary>
    public sealed class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());

            // The screens paint a fixed dark palette, so the few stock controls are
            // pinned to the dark variant rather than following the system setting
            // and clashing with everything drawn around them.
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                AppSettings settings = AppSettings.Load();
                ApplyEndpointOverrides();
                ApplyUserAgent(settings);

                singleView.MainView = new PhoneShell(settings);
            }

            base.OnFrameworkInitializationCompleted();
        }

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
