using System.Windows.Forms;
using WeatherApp.Models;

namespace WeatherApp.UI
{
    /// <summary>
    /// Base for the tab contents.
    ///
    /// Every tab is handed the same snapshot and decides what to show from it, so
    /// a refresh is one assignment rather than a fan-out of setters. Double
    /// buffering is switched on here because all of these panels repaint whole
    /// custom-drawn surfaces and would otherwise flicker badly on the older,
    /// unaccelerated machines this app is aimed at.
    /// </summary>
    public abstract class WeatherPanel : UserControl
    {
        protected WeatherSnapshot Snapshot { get; private set; }

        protected WeatherPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);

            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
        }

        public virtual void ApplySnapshot(WeatherSnapshot snapshot)
        {
            Snapshot = snapshot;
            OnSnapshotChanged();
            Invalidate();
        }

        /// <summary>Hook for panels that need to rebuild child controls on new data.</summary>
        protected virtual void OnSnapshotChanged()
        {
        }

        /// <summary>Called when the tab becomes visible, for panels that load lazily.</summary>
        public virtual void OnActivated()
        {
        }
    }
}
