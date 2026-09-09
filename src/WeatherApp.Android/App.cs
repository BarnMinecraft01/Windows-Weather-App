using System;
using System.IO;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
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
            AppSettings settings = LoadSettings();

            // Avalonia 12 hands Android an IActivityApplicationLifetime with a
            // factory rather than a single view, because the platform may create
            // more than one activity and each needs its own view. The settings are
            // loaded once and captured, so every activity reads the same places.
            if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                activityLifetime.MainViewFactory = () => CreateMainView(settings);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = CreateMainView(settings);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Builds the phone shell, or a screen explaining why it could not be built.
        ///
        /// An exception escaping here takes the whole process down and the user sees
        /// the app open and close with no explanation. Catching it turns the worst
        /// class of bug -- a launch crash on a device nobody testing this owns --
        /// into something a person can read and report.
        /// </summary>
        private static Control CreateMainView(AppSettings settings)
        {
            try
            {
                return new PhoneShell(settings);
            }
            catch (Exception ex)
            {
                CrashReporter.Report("PhoneShell constructor", ex);
                return ErrorView.For(ex, "PhoneShell constructor");
            }
        }

        private static AppSettings LoadSettings()
        {
            try
            {
                AppSettings settings = AppSettings.Load();
                ApplyEndpointOverrides();
                ApplyUserAgent(settings);
                return settings;
            }
            catch (Exception ex)
            {
                // Settings live under a platform-resolved path; if that resolution
                // fails the app should still start with defaults rather than die.
                CrashReporter.Report("settings", ex);
                return new AppSettings();
            }
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
