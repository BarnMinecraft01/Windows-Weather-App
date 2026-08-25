using System;
using System.Drawing;
using System.Windows.Forms;
using WeatherApp.Platform;

namespace WeatherApp.UI
{
    /// <summary>
    /// Windows implementation of <see cref="INotifier"/>: a tray balloon.
    ///
    /// The icon is only made visible when there is something to say, so the app
    /// does not sit in the notification area during quiet weather.
    /// </summary>
    public sealed class TrayNotifier : INotifier, IDisposable
    {
        private readonly NotifyIcon _icon;

        public TrayNotifier()
        {
            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = false,
                Text = "Windows Weather"
            };
        }

        public void Notify(string title, string message, bool urgent)
        {
            _icon.Visible = true;
            _icon.BalloonTipIcon = urgent ? ToolTipIcon.Warning : ToolTipIcon.Info;
            _icon.BalloonTipTitle = title ?? "Weather alert";
            _icon.BalloonTipText = message ?? string.Empty;
            _icon.ShowBalloonTip(10000);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
