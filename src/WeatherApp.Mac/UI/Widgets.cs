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
                Background = Theme.SurfaceAltBrush,
                Foreground = Theme.Text,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeBody,
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
                Background = Theme.SurfaceAltBrush,
                Foreground = Theme.Text,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeBody,
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
                Foreground = Theme.TextFaint,
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeSmall,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock Label(string text, IBrush brush = null, double? size = null)
        {
            return new TextBlock
            {
                Text = text ?? string.Empty,
                Foreground = brush ?? Theme.TextMuted,
                FontFamily = Theme.UiFont,
                FontSize = size ?? Theme.SizeSmall,
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
                Foreground = Theme.Text,
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeBody,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBox Input(double width)
        {
            return new TextBox
            {
                Width = width,
                Height = 28,
                Background = Theme.SurfaceAltBrush,
                Foreground = Theme.Text,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeBody
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
                Background = Theme.SurfaceBrush,
                Foreground = Theme.Text,
                BorderThickness = new Thickness(0),
                FontFamily = Theme.UiFont,
                FontSize = Theme.SizeBody
            };
        }

        /// <summary>
        /// Opens a URL in the user's browser.
        ///
        /// UseShellExecute must be set explicitly: it defaults to false on .NET,
        /// where a bare URL then throws. With it set, macOS routes through `open`
        /// and Windows through the shell handler.
        /// </summary>
        public static bool OpenUrl(string url)
        {
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
