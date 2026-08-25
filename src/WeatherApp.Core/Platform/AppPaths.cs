using System;
using System.IO;

// The platform guards in this file and in Net/HttpService.cs select behaviour at
// compile time, so a head that defines neither symbol would silently compile the
// wrong branch out rather than fail. SDK-style projects define these implicitly;
// the classic WinForms csproj has to declare NETFRAMEWORK by hand, and this turns
// forgetting that into a build error instead of a Windows 7 TLS regression.
#if !NETFRAMEWORK && !NET
#error Neither NETFRAMEWORK nor NET is defined. Add NETFRAMEWORK to DefineConstants for the .NET Framework head.
#endif

namespace WeatherApp.Platform
{
    /// <summary>
    /// Where the app keeps its settings, endpoint overrides and error log.
    ///
    /// Each desktop platform has its own convention and none of them is the
    /// default that <see cref="Environment.SpecialFolder.ApplicationData"/> gives
    /// you everywhere: on macOS that constant resolves to ~/.config, which is a
    /// Linux convention that Mac users will never think to look in.
    ///
    /// The .NET Framework head only ever runs on Windows, so its branch is
    /// compiled in directly -- which also sidesteps the fact that
    /// RuntimeInformation.IsOSPlatform did not arrive until .NET Framework 4.7.1
    /// and is unavailable at the 4.6.2 floor this project targets.
    /// </summary>
    public static class AppPaths
    {
        private const string FolderName = "WindowsWeatherApp";

        public static string SettingsDirectory
        {
            get
            {
#if NETFRAMEWORK
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
#else
                // Android has no user home in the desktop sense; the app's private
                // files directory is the only place it may write unconditionally,
                // and LocalApplicationData maps to exactly that.
                if (OperatingSystem.IsAndroid())
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        FolderName);
                }

                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    // The documented location for per-user application data on macOS.
                    return Path.Combine(home, "Library", "Application Support", FolderName);
                }

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
                }

                // Linux and anything else: honour XDG_CONFIG_HOME when it is set.
                string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                string configRoot = string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".config") : xdg;
                return Path.Combine(configRoot, FolderName);
#endif
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(SettingsDirectory, "settings.json"); }
        }

        public static string EndpointsFile
        {
            get { return Path.Combine(SettingsDirectory, "endpoints.json"); }
        }

        public static string ErrorLogFile
        {
            get { return Path.Combine(SettingsDirectory, "error.log"); }
        }

        /// <summary>Creates the settings directory, swallowing the failures that are not worth surfacing.</summary>
        public static bool EnsureDirectory()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
