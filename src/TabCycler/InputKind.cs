using System;

namespace TabCycler
{
    /// <summary>
    /// What the user did. The low-level input hook can tell these apart;
    /// <see cref="GetLastInputInfo"/> cannot, which is why the hook exists.
    /// </summary>
    public enum InputKind
    {
        /// <summary>Nothing observed.</summary>
        None = 0,

        /// <summary>A key going down.</summary>
        KeyPress,

        /// <summary>A mouse button going down.</summary>
        MouseClick,

        /// <summary>A wheel or horizontal wheel event.</summary>
        Scroll,

        /// <summary>The pointer moved without a button held.</summary>
        Movement,

        /// <summary>
        /// Known to be one of KeyPress, MouseClick or Scroll, but which one is
        /// unknown. Only produced by the fallback path when the hook could not
        /// be installed, because GetLastInputInfo reports that input happened
        /// and nothing more. Treated as matching any of those three toggles.
        /// </summary>
        Unclassified
    }

    /// <summary>One observed input, with enough detail to filter it.</summary>
    public struct InputEvent
    {
        public readonly InputKind Kind;

        /// <summary>
        /// True when the event was synthesised rather than produced by real
        /// hardware. This is the <c>LLKHF_INJECTED</c> /
        /// <c>LLMHF_INJECTED</c> flag, and it is what lets the cycler ignore
        /// its own keystrokes and any other process driving the mouse, instead
        /// of having to guess from an input timestamp.
        /// </summary>
        public readonly bool Injected;

        public readonly CursorPos Position;

        public InputEvent(InputKind kind, bool injected, CursorPos position)
        {
            Kind = kind;
            Injected = injected;
            Position = position;
        }

        public override string ToString()
        {
            return Kind + (Injected ? " (injected)" : "");
        }
    }
}
