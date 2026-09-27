using System;

namespace TabCycler
{
    /// <summary>
    /// Which inputs re-arm the hold, and whether synthesised input counts.
    /// Defaults preserve the behaviour the tool shipped with: everything counts,
    /// except injected input, which is ignored so that the cycler's own
    /// keystrokes and any other process driving the mouse do not read as the
    /// user.
    /// </summary>
    public struct InputOptions : IEquatable<InputOptions>
    {
        public bool ResetOnKeyPress;
        public bool ResetOnClick;
        public bool ResetOnScroll;
        public bool ResetOnMovement;

        /// <summary>
        /// Ignore events flagged as synthesised. True by default because the
        /// cycler injects its own Ctrl+Tab every cycle, and because other
        /// programs (browser-automation agents, remote-control tooling) can
        /// drive the real mouse, which otherwise holds the widget off forever.
        /// </summary>
        public bool IgnoreInjected;

        public static InputOptions Default
        {
            get
            {
                InputOptions o = new InputOptions();
                o.ResetOnKeyPress = true;
                o.ResetOnClick = true;
                o.ResetOnScroll = true;
                o.ResetOnMovement = true;
                o.IgnoreInjected = true;
                return o;
            }
            }

        /// <summary>
        /// Whether an event of this kind should re-arm the hold. An
        /// <see cref="InputKind.Unclassified"/> event, which only the fallback
        /// path can produce, counts if any of the three it could be is enabled,
        /// because that is the honest answer given the available information.
        /// </summary>
        public bool ArmsFor(InputKind kind)
        {
            switch (kind)
            {
                case InputKind.KeyPress: return ResetOnKeyPress;
                case InputKind.MouseClick: return ResetOnClick;
                case InputKind.Scroll: return ResetOnScroll;
                case InputKind.Movement: return ResetOnMovement;
                case InputKind.Unclassified:
                    return ResetOnKeyPress || ResetOnClick || ResetOnScroll;
                default: return false;
            }
        }

        public void Validate()
        {
            // Every combination is legal, including all-off, which means "never
            // hold on input". Nothing to reject.
        }

        public bool Equals(InputOptions other)
        {
            return ResetOnKeyPress == other.ResetOnKeyPress
                && ResetOnClick == other.ResetOnClick
                && ResetOnScroll == other.ResetOnScroll
                && ResetOnMovement == other.ResetOnMovement
                && IgnoreInjected == other.IgnoreInjected;
        }

        public override bool Equals(object obj)
        {
            return obj is InputOptions && Equals((InputOptions)obj);
        }

        public override int GetHashCode()
        {
            int h = 17;
            h = h * 31 + (ResetOnKeyPress ? 1 : 0);
            h = h * 31 + (ResetOnClick ? 1 : 0);
            h = h * 31 + (ResetOnScroll ? 1 : 0);
            h = h * 31 + (ResetOnMovement ? 1 : 0);
            h = h * 31 + (IgnoreInjected ? 1 : 0);
            return h;
        }
    }
}
