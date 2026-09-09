using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WeatherApp.Droid
{
    /// <summary>
    /// A last-resort screen that shows why the app failed to start.
    ///
    /// Built from raw Avalonia primitives with literal colours and no reference to
    /// the app's own palette or widget helpers -- those are candidates for having
    /// caused the failure, and an error screen that can itself throw is worse than
    /// none. The text is selectable so it can be copied into a bug report.
    /// </summary>
    internal static class ErrorView
    {
        public static Control For(Exception exception, string context)
        {
            var body = new SelectableTextBlock
            {
                Text = Compose(exception, context),
                FontSize = 12,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16)
            };

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x10, 0x10)),
                Child = new ScrollViewer { Content = body }
            };
        }

        private static string Compose(Exception exception, string context)
        {
            return "Windows Weather could not start."
                 + Environment.NewLine + Environment.NewLine
                 + "This text has also been written to:" + Environment.NewLine
                 + CrashReporter.CrashLogPath
                 + Environment.NewLine + Environment.NewLine
                 + "Press and hold to select and copy it."
                 + Environment.NewLine + Environment.NewLine
                 + CrashReporter.Format(context, exception);
        }
    }
}
