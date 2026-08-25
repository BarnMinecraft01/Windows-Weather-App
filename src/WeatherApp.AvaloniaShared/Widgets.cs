using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WeatherApp.UI
{
    /// <summary>
    /// Factories for the handful of stock controls the app uses, pre-styled to
    /// match the drawn surfaces around them.
    ///
    /// Kept in one place so the toolbar in the main window and the controls inside
    /// the radar and jet stream tabs cannot drift apart visually.
    /// </summary>
    internal static class Widgets
    {
        public static ComboBox Combo(double width)
        {
            return new ComboBox
            {
                Width = width,
                Height = 28,
                Background = AppTheme.SurfaceAltBrush,
                Foreground = AppTheme.Text,
                BorderBrush = AppTheme.Brush(AppTheme.BorderColor),
                BorderThickness = new Thickness(1),
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Button Button(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                Background = AppTheme.SurfaceAltBrush,
                Foreground = AppTheme.Text,
                BorderBrush = AppTheme.Brush(AppTheme.BorderColor),
                BorderThickness = new Thickness(1),
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
        }

        public static TextBlock Caption(string text)
        {
            return new TextBlock
            {
                Text = (text ?? string.Empty).ToUpperInvariant(),
                Foreground = AppTheme.TextFaint,
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeSmall,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock Label(string text, IBrush brush = null, double? size = null)
        {
            return new TextBlock
            {
                Text = text ?? string.Empty,
                Foreground = brush ?? AppTheme.TextMuted,
                FontFamily = AppTheme.UiFont,
                FontSize = size ?? AppTheme.SizeSmall,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        public static CheckBox Check(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = AppTheme.Text,
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBox Input(double width)
        {
            return new TextBox
            {
                Width = width,
                Height = 28,
                Background = AppTheme.SurfaceAltBrush,
                Foreground = AppTheme.Text,
                BorderBrush = AppTheme.Brush(AppTheme.BorderColor),
                BorderThickness = new Thickness(1),
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody
            };
        }

        /// <summary>Read-only multi-line text with its own scrollbar, for alert bodies.</summary>
        public static TextBox ReadOnlyText()
        {
            return new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = AppTheme.SurfaceBrush,
                Foreground = AppTheme.Text,
                BorderThickness = new Thickness(0),
                FontFamily = AppTheme.UiFont,
                FontSize = AppTheme.SizeBody
            };
        }

        /// <summary>
        /// Opens a URL outside the app.
        ///
        /// Prefers Avalonia's launcher, which is the only route that works on
        /// Android -- there is no process to start there, the platform expects an
        /// Intent. Falls back to the shell on desktop, where UseShellExecute has
        /// to be set explicitly because it defaults to false on .NET and a bare
        /// URL then throws.
        /// </summary>
        public static bool OpenUrl(string url, Control owner = null)
        {
            if (owner != null)
            {
                try
                {
                    TopLevel topLevel = TopLevel.GetTopLevel(owner);
                    if (topLevel != null)
                    {
                        // Fire and forget: the launcher hands off to the OS and the
                        // result tells us nothing the user cannot already see.
                        _ = topLevel.Launcher.LaunchUriAsync(new Uri(url));
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Fall through to the desktop path below.
                }
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
