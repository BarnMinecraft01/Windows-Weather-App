using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Graphics;

namespace WeatherApp.Droid
{
    /// <summary>
    /// A report screen built entirely from Android's own views.
    ///
    /// Deliberately shares nothing with the rest of the app. ErrorView is an
    /// Avalonia control, so it can only be shown once Avalonia has started -- and
    /// the failure this exists for is precisely the one where Avalonia never
    /// starts, or where the app's own Application object never runs at all. A
    /// screen made of TextView and Button has no such dependency: if Android can
    /// launch an activity, this displays.
    /// </summary>
    [Activity(
        Label = "Windows Weather",
        Exported = false)]
    public class CrashActivity : Activity
    {
        public const string ReportExtra = "report";

        private string _report;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _report = Intent?.GetStringExtra(ReportExtra) ?? "No report was supplied.";

            var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            root.SetBackgroundColor(Color.Rgb(0x14, 0x18, 0x20));

            root.AddView(Heading("Windows Weather did not start"));
            root.AddView(Heading(
                "The details below are what the last launch managed to record. "
                + "Share or copy them -- they are what makes the cause findable."));

            var buttons = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            buttons.SetPadding(24, 8, 24, 8);
            buttons.AddView(MakeButton("Share", (s, e) => Share()));
            buttons.AddView(MakeButton("Copy", (s, e) => Copy()));
            buttons.AddView(MakeButton("Start anyway", (s, e) => StartAnyway()));
            root.AddView(buttons);

            var body = new TextView(this)
            {
                Text = _report,
                TextSize = 11,
                Typeface = Typeface.Monospace
            };
            body.SetTextColor(Color.Rgb(0xE6, 0xE8, 0xEC));
            body.SetPadding(24, 8, 24, 48);
            body.SetTextIsSelectable(true);

            var scroller = new ScrollView(this);
            scroller.AddView(body);

            // The scroller takes the remaining height, so the buttons stay put
            // however long the report is.
            root.AddView(scroller, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));

            SetContentView(root);
        }

        private TextView Heading(string text)
        {
            var view = new TextView(this) { Text = text, TextSize = 14 };
            view.SetTextColor(Color.Rgb(0xFF, 0xC8, 0x6B));
            view.SetPadding(24, 16, 24, 0);
            return view;
        }

        private Button MakeButton(string text, EventHandler handler)
        {
            var button = new Button(this) { Text = text };
            button.Click += handler;
            return button;
        }

        private void Share()
        {
            try
            {
                var intent = new Intent(Intent.ActionSend);
                intent.SetType("text/plain");
                intent.PutExtra(Intent.ExtraSubject, "Windows Weather launch failure");
                intent.PutExtra(Intent.ExtraText, _report);
                StartActivity(Intent.CreateChooser(intent, "Share the report"));
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Could not share: " + ex.Message, ToastLength.Long).Show();
            }
        }

        private void Copy()
        {
            try
            {
                var clipboard = (ClipboardManager)GetSystemService(ClipboardService);
                clipboard.PrimaryClip = ClipData.NewPlainText("Windows Weather", _report);
                Toast.MakeText(this, "Report copied", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Could not copy: " + ex.Message, ToastLength.Long).Show();
            }
        }

        /// <summary>
        /// Tries the real app anyway. The failure may have been transient, and
        /// being able to get past this screen matters more than being certain.
        /// </summary>
        private void StartAnyway()
        {
            try
            {
                StartActivity(new Intent(this, typeof(MainActivity)));
                Finish();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Could not start: " + ex.Message, ToastLength.Long).Show();
            }
        }
    }
}
