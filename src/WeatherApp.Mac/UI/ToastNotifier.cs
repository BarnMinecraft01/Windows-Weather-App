using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using WeatherApp.Platform;

namespace WeatherApp.UI
{
    /// <summary>
    /// macOS/Linux implementation of <see cref="INotifier"/>: an in-window toast.
    ///
    /// Avalonia's own notification manager is used rather than a native macOS
    /// notification. A real Notification Center alert needs a signed, bundled app
    /// and an extra native dependency, and it would silently do nothing in an
    /// unsigned development build -- which is a worse failure than a toast the
    /// user can definitely see.
    /// </summary>
    public sealed class ToastNotifier : INotifier
    {
        private readonly WindowNotificationManager _manager;

        public ToastNotifier(TopLevel topLevel)
        {
            _manager = new WindowNotificationManager(topLevel)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 4
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
