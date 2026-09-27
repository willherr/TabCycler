using System;
using System.Drawing;
using System.Windows.Forms;

namespace TabCycler
{
    /// <summary>
    /// Shared per-monitor DPI behaviour for every window in this app.
    ///
    /// This exists because the widget and the settings dialog must agree, and
    /// because the first version of this logic got the trigger wrong. The
    /// original widget re-laid out from <c>OnDpiChangedAfterParent</c>, which is
    /// the child-control hook, so for a top-level form it never fired and
    /// dragging between a 150% and a 100% monitor resized nothing. A copy
    /// pasted into a second form would have inherited the same defect, so the
    /// trigger lives here once.
    ///
    /// The contract each window implements is <see cref="OnDpiScaled"/>: lay out
    /// for the given scale, and only then. Do not re-read the DPI from inside
    /// it, because the whole bug is that two reads disagree.
    /// </summary>
    internal abstract class DpiAwareForm : Form
    {
        const int WM_DPICHANGED = 0x02E0;

        /// <summary>Multiplier for geometry authored at 96 DPI.</summary>
        protected float DpiScale { get; private set; }

        /// <summary>The DPI the current layout was built for.</summary>
        protected uint CurrentDpi { get; private set; }

        protected DpiAwareForm()
        {
            // Deliberately None. WinForms' own DPI autoscaling reads the DPI via
            // CreateGraphics(), which reports 96 on a 150% display on this
            // machine, so it silently declines to scale. This class does it
            // properly instead.
            AutoScaleMode = AutoScaleMode.None;
        }

        /// <summary>Scales a value authored at 96 DPI, ignoring Windows rounding.</summary>
        protected int P96(int v)
        {
            return (int)Math.Round(v * DpiScale, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// A font whose point size has been scaled to the current DPI. WinForms
        /// realises a point-size font against the handle's own DPI at paint time,
        /// so passing a scaled size here and letting it scale again would
        /// double-scale the text. Instead the callers use raw point sizes and
        /// this is only for the rare case of needing to match a scaled box.
        /// </summary>
        protected static Font ScaledFont(float points, FontStyle style, float scale)
        {
            return new Font("Segoe UI", points * scale, style);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDpi(Native.GetDpiForWindow(Handle), "GetDpiForWindow");
        }

        /// <summary>
        /// Re-reads the DPI once the window is actually on screen. The first
        /// layout happens before the window is positioned, so the value then can
        /// describe a monitor the window is not on.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyDpi(Native.GetDpiForWindow(Handle), "shown");
        }

        /// <summary>
        /// WM_DPICHANGED is the authoritative source for a top-level window, and
        /// LOWORD(wParam) is the new DPI. base.WndProc runs first so WinForms
        /// updates its own bookkeeping before the layout is recomputed. The
        /// suggested rectangle in lParam is deliberately not applied: the widget
        /// owns its own position and honouring it would fight the drag handler.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                uint dpi = (uint)(m.WParam.ToInt64() & 0xFFFF);
                base.WndProc(ref m);
                ApplyDpi(dpi, "WM_DPICHANGED");
                return;
            }
            base.WndProc(ref m);
        }

        void ApplyDpi(uint dpi, string source)
        {
            if (dpi == 0) dpi = 96;
            bool first = CurrentDpi == 0;
            CurrentDpi = dpi;
            DpiScale = dpi / 96f;

            OnDpiScaled(dpi, DpiScale, source);

            // The first pass is not worth a log line; the second one is the one
            // that explains a surprise, and it names the trigger that fired.
            if (!first || source == "WM_DPICHANGED")
                LogDpi("dpi=" + dpi + " (scale " + DpiScale.ToString("0.###") +
                       ") via " + source);
        }

        /// <summary>Lay the window out for this scale.</summary>
        protected abstract void OnDpiScaled(uint dpi, float scale, string source);

        /// <summary>
        /// Where DPI changes are reported. The widget sends them to its log
        /// because the log is the only place the scale is visible when the UI is
        /// too small to read.
        /// </summary>
        protected virtual void LogDpi(string message)
        {
            System.Diagnostics.Debug.WriteLine("TabCycler: " + message);
        }
    }
}
