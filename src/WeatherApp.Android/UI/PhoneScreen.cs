using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace WeatherApp.UI
{
    /// <summary>
    /// Base for the phone screens: a drawn surface that scrolls under the finger.
    ///
    /// Drag scrolling is implemented here rather than by wrapping each screen in a
    /// ScrollViewer, because the screens draw their content instead of composing
    /// it from controls -- there is no laid-out child for a ScrollViewer to
    /// measure. Translating the drawing context is both simpler and cheaper.
    ///
    /// A short press that does not move is forwarded as a tap, in content
    /// coordinates, so screens can offer touch targets without each one having to
    /// separate taps from drags.
    /// </summary>
    public abstract class PhoneScreen : WeatherPanel
    {
        /// <summary>Movement beyond this many points means the user is scrolling, not tapping.</summary>
        private const double TapSlop = 12;

        private double _scroll;
        private double _dragOrigin;
        private Point _pressedAt;
        private bool _pressed;
        private bool _moved;

        protected PhoneScreen()
        {
            PointerPressed += (s, e) =>
            {
                _pressed = true;
                _moved = false;
                _pressedAt = e.GetPosition(this);
                _dragOrigin = _scroll;
            };

            PointerMoved += (s, e) =>
            {
                if (!_pressed) return;

                double delta = _pressedAt.Y - e.GetPosition(this).Y;
                if (Math.Abs(delta) > TapSlop) _moved = true;
                if (!_moved) return;

                _scroll = _dragOrigin + delta;
                ClampScroll();
                InvalidateSurface();
            };

            PointerReleased += (s, e) =>
            {
                if (!_pressed) return;
                _pressed = false;

                if (_moved) return;

                // A tap: hand it over in content coordinates, undoing the scroll.
                Point point = e.GetPosition(this);
                OnTap(new Point(point.X, point.Y + _scroll));
            };

            // Present when the app runs on a desktop for development.
            PointerWheelChanged += (s, e) =>
            {
                _scroll -= e.Delta.Y * 60;
                ClampScroll();
                InvalidateSurface();
            };
        }

        /// <summary>Total drawn height, used to bound scrolling.</summary>
        protected abstract double ContentHeight { get; }

        /// <summary>Draws the screen. The context is already translated for scroll.</summary>
        protected abstract void DrawContent(DrawingContext context, double width);

        /// <summary>A tap at a point in content coordinates. Does nothing by default.</summary>
        protected virtual void OnTap(Point point)
        {
        }

        /// <summary>Returns to the top, e.g. when the shown location changes.</summary>
        protected void ResetScroll()
        {
            _scroll = 0;
        }

        protected sealed override void DrawSurface(DrawingContext context)
        {
            ClampScroll();

            using (context.PushTransform(Matrix.CreateTranslation(0, -_scroll)))
            {
                DrawContent(context, W);
            }

            DrawScrollIndicator(context);
        }

        /// <summary>
        /// A slim bar down the right edge while there is more to see. Phones give
        /// no other hint that content continues past the fold.
        /// </summary>
        private void DrawScrollIndicator(DrawingContext context)
        {
            double overflow = ContentHeight - H;
            if (overflow <= 4 || H <= 0) return;

            double trackHeight = H - 16;
            double thumbHeight = Math.Max(28, trackHeight * (H / ContentHeight));
            double progress = _scroll / overflow;
            double thumbTop = 8 + (trackHeight - thumbHeight) * Math.Min(1, Math.Max(0, progress));

            context.DrawRectangle(AppTheme.Brush(AppTheme.TextFaintColor, 120), null,
                new Rect(W - 5, thumbTop, 3, thumbHeight), 1.5, 1.5);
        }

        private void ClampScroll()
        {
            double maximum = Math.Max(0, ContentHeight - H);
            if (_scroll > maximum) _scroll = maximum;
            if (_scroll < 0) _scroll = 0;
        }
    }
}
