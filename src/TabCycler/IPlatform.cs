using System;

namespace TabCycler
{
    /// <summary>
    /// Everything the state machine needs from the outside world. Keeping this
    /// behind an interface is what makes the machine testable: a test supplies
    /// a fake and drives focus, input stamps and cursor movement directly,
    /// with no desktop, no window handles and no injected keystrokes.
    /// </summary>
    public interface IPlatform
    {
        /// <summary>The current foreground top-level window, or IntPtr.Zero.</summary>
        IntPtr GetForegroundWindow();

        /// <summary>Whether a window handle belongs to Windows Terminal.</summary>
        bool IsTerminalWindow(IntPtr hwnd);

        /// <summary>
        /// Tick stamp of the most recent keyboard or mouse input anywhere on the
        /// system. Compared against the stamp taken after each injected
        /// keystroke, so the cycler's own input is not read as the user.
        /// </summary>
        uint LastInputStamp();

        /// <summary>Current pointer position, used to tell input from drift.</summary>
        CursorPos CursorPosition();

        /// <summary>Switch the terminal to its next tab, as Ctrl+Tab would.</summary>
        void InjectNextTab();
    }
}
