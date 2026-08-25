using System;
using System.IO;
using System.Windows.Forms;
using WeatherApp.Configuration;
using WeatherApp.Net;
using WeatherApp.Platform;
using WeatherApp.UI;

namespace WeatherApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // An unhandled exception in a weather app should not lose the window.
            // Both handlers are installed because WinForms routes UI-thread faults
            // through ThreadException and everything else through the AppDomain.
            Application.ThreadException += (s, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);

            AppSettings settings = AppSettings.Load();
            ApplyEndpointOverrides();
            ApplyUserAgent(settings);

            Application.Run(new MainForm(settings));
        }

        /// <summary>
        /// Loads endpoints.json if the user has one, and writes a template on first
        /// run so there is something to edit if a NOAA product path ever moves.
        /// </summary>
        private static void ApplyEndpointOverrides()
        {
            try
            {
                AppPaths.EnsureDirectory();

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

        /// <summary>
        /// The NWS API asks callers to identify themselves and throttles traffic
        /// that does not. If the user has supplied a contact it is appended.
        /// </summary>
        private static void ApplyUserAgent(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ContactForUserAgent)) return;

            HttpService.UserAgent = "WindowsWeatherApp/1.0 ("
                + settings.ContactForUserAgent.Trim() + "; "
                + "+https://github.com/BarnMinecraft01/Windows-Weather-App)";
        }

        private static void ReportCrash(Exception exception)
        {
            string message = exception == null
                ? "An unknown error occurred."
                : exception.Message;

            try
            {
                string logPath = AppPaths.ErrorLogFile;
                AppPaths.EnsureDirectory();

                File.AppendAllText(logPath,
                    DateTime.Now.ToString("u") + Environment.NewLine
                    + (exception == null ? "(no exception)" : exception.ToString())
                    + Environment.NewLine + Environment.NewLine);

                message += Environment.NewLine + Environment.NewLine + "Details were written to:"
                           + Environment.NewLine + logPath;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            MessageBox.Show(message, "Windows Weather", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
