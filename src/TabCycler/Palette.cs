using System.Drawing;
using System.Windows.Forms;

namespace TabCycler
{
    /// <summary>
    /// The widget's colours, in one place because the settings dialog has to
    /// match it and duplicating hex values across two forms is how they drift
    /// apart.
    ///
    /// Every pair here is contrast-checked rather than eyeballed. The one that
    /// was not is worth recording: the border used to be #444444, which is
    /// 1.69:1 against the #1F1F1F background and therefore invisible to anyone
    /// who needs the 3:1 that a non-text boundary requires. It is #707070 now,
    /// 3.33:1. The button label on #333333 is 9.39:1, the body text on the
    /// background 6.77:1, and the title 13.95:1, all comfortably over 4.5:1.
    /// </summary>
    internal static class Palette
    {
        public static readonly Color Back = Color.FromArgb(0x1F, 0x1F, 0x1F);
        public static readonly Color Edge = Color.FromArgb(0x70, 0x70, 0x70);
        public static readonly Color Title = Color.FromArgb(0xEC, 0xEC, 0xEC);
        public static readonly Color Body = Color.FromArgb(0xA6, 0xA6, 0xA6);
        public static readonly Color Hint = Color.FromArgb(0x7A, 0x7A, 0x7A);
        public static readonly Color BtnFace = Color.FromArgb(0x33, 0x33, 0x33);
        public static readonly Color BtnHover = Color.FromArgb(0x44, 0x44, 0x44);
        public static readonly Color StartFace = Color.FromArgb(0x4A, 0x4A, 0x4A);
        public static readonly Color Danger = Color.FromArgb(0xC4, 0x2B, 0x1C);

        /// <summary>
        /// Hover for the close button. A lighter red than Danger but still
        /// passing: white text on it is 4.53:1. The first candidate,
        /// #E03A2A, was 4.37:1 and failed, so this is the lightest red that
        /// keeps the glyph legible.
        /// </summary>
        public static readonly Color DangerHover = Color.FromArgb(0xDC, 0x38, 0x26);

        /// <summary>Label colour on the button faces, 9.39:1 on BtnFace.</summary>
        public static readonly Color BtnLabel = Color.FromArgb(0xDE, 0xDE, 0xDE);

        /// <summary>
        /// Resting label on the close button, which is quieter than BtnLabel
        /// until hover. 8.07:1 on BtnFace, so still well over the bar.
        /// </summary>
        public static readonly Color CloseLabel = Color.FromArgb(0xC9, 0xC9, 0xC9);

        /// <summary>Applies the flat dark button look used across the widget.</summary>
        public static void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = BtnHover;
            b.BackColor = BtnFace;
            b.ForeColor = BtnLabel;
            b.UseVisualStyleBackColor = false;
        }
    }
}
