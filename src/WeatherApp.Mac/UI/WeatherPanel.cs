using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Base for the tab contents, the Avalonia counterpart of the WinForms
    /// WeatherPanel.
    ///
    /// Canvas is the base class because it gives absolute child placement, which
    /// keeps the layout code identical to the WinForms head rather than forcing a
    /// rewrite into panels and grids for no visual gain. A Background is set
    /// explicitly because a control with a null background is invisible to hit
    /// testing in Avalonia, and several panels handle pointer input.
    /// </summary>
    public abstract class WeatherPanel : Canvas
    {
        protected WeatherSnapshot Snapshot { get; private set; }

        protected WeatherPanel()
        {
            Background = Theme.BackgroundBrush;
            ClipToBounds = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            SizeChanged += (s, e) =>
            {
                LayoutChildren();
                InvalidateVisual();
            };
        }

        public double W { get { return Bounds.Width; } }
        public double H { get { return Bounds.Height; } }

        public virtual void ApplySnapshot(WeatherSnapshot snapshot)
        {
            Snapshot = snapshot;
            OnSnapshotChanged();
            LayoutChildren();
            InvalidateVisual();
        }

        /// <summary>Hook for panels that rebuild child controls when new data lands.</summary>
        protected virtual void OnSnapshotChanged()
        {
        }

        /// <summary>Positions child controls; called on every resize.</summary>
        protected virtual void LayoutChildren()
        {
        }

        /// <summary>Called when the tab becomes visible, for panels that load lazily.</summary>
        public virtual void OnActivated()
        {
        }

        protected static void Place(Control control, double left, double top, double width, double height)
        {
            Canvas.SetLeft(control, left);
            Canvas.SetTop(control, top);
            control.Width = Math.Max(0, width);
            control.Height = Math.Max(0, height);
        }

        /// <summary>Convenience for the "no data yet" state every panel shows.</summary>
        protected void DrawPlaceholder(DrawingContext context, string message)
        {
            Theme.DrawText(context, message, Theme.Regular, Theme.SizeBody, Theme.TextMuted,
                new Rect(0, 0, W, H), TextAlignment.Center, middle: true);
        }
    }
}
