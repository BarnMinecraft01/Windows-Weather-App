namespace WeatherApp.Platform
{
    /// <summary>
    /// How the app tells the user a warning has just been issued.
    ///
    /// The two heads do genuinely different things here -- a Windows tray balloon
    /// and an in-window toast on macOS -- and it is the one piece of user-facing
    /// behaviour that cannot be shared, so it is an interface rather than a
    /// compile-time switch.
    /// </summary>
    public interface INotifier
    {
        /// <param name="title">The alert's event name, e.g. "Tornado Warning".</param>
        /// <param name="message">Area covered, or a count when several arrived at once.</param>
        /// <param name="urgent">True for warnings, false for watches and advisories.</param>
        void Notify(string title, string message, bool urgent);
    }

    /// <summary>Used when notifications are switched off, so callers need no null checks.</summary>
    public sealed class NullNotifier : INotifier
    {
        public void Notify(string title, string message, bool urgent)
        {
        }
    }
}
