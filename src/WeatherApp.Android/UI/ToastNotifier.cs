using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using WeatherApp.Platform;

namespace WeatherApp.UI
{
    /// <summary>
    /// Android implementation of <see cref="INotifier"/>: an in-window toast.
    ///
    /// Deliberately not a system notification. A Notification Center entry that is
    /// useful when the app is closed needs a notification channel plus a
    /// foreground service or WorkManager job to poll for alerts, which is real
    /// Android-specific work rather than a UI detail. Until that exists, a toast
    /// the user can actually see beats a system call that silently does nothing.
    /// </summary>
    public sealed class ToastNotifier : INotifier
    {
        private readonly WindowNotificationManager _manager;

        public ToastNotifier(TopLevel topLevel)
        {
            _manager = new WindowNotificationManager(topLevel)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3
            };
        }

        public void Notify(string title, string message, bool urgent)
        {
            _manager.Show(new Notification(
                title ?? "Weather alert",
                message ?? string.Empty,
                urgent ? NotificationType.Warning : NotificationType.Information));
        }
    }
}
