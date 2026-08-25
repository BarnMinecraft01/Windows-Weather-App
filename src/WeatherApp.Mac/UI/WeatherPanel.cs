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

        private readonly Surface _surface;

        protected WeatherPanel()
        {
            Background = Theme.BackgroundBrush;
            ClipToBounds = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            // Avalonia seals Panel.Render, so a Canvas cannot draw its own content.
            // The custom painting therefore lives on a plain Control that fills the
            // panel and delegates back to DrawSurface. It goes in first so it sits
            // behind the real child controls, and it is hit-test invisible so
            // pointer events fall through to the panel that handles them.
            _surface = new Surface(this) { IsHitTestVisible = false };
            Children.Add(_surface);

            SizeChanged += (s, e) =>
            {
                Place(_surface, 0, 0, Bounds.Width, Bounds.Height);
                LayoutChildren();
                InvalidateSurface();
            };
        }

        /// <summary>Repaints the drawn content. The Canvas itself has nothing to repaint.</summary>
        public void InvalidateSurface()
        {
            _surface.InvalidateVisual();
        }

        /// <summary>Draws this panel's content. The counterpart of OnPaint in the WinForms head.</summary>
        protected abstract void DrawSurface(DrawingContext context);

        private sealed class Surface : Control
        {
            private readonly WeatherPanel _owner;

            public Surface(WeatherPanel owner)
            {
                _owner = owner;
            }

            public override void Render(DrawingContext context)
            {
                _owner.DrawSurface(context);
            }
        }

        public double W { get { return Bounds.Width; } }
        public double H { get { return Bounds.Height; } }

        public virtual void ApplySnapshot(WeatherSnapshot snapshot)
        {
            Snapshot = snapshot;
            OnSnapshotChanged();
            LayoutChildren();
            InvalidateSurface();
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
