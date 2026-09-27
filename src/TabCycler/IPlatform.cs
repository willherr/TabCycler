using System;
using System.Collections.Generic;

namespace TabCycler
{
    /// <summary>
    /// Everything the state machine needs from the outside world. Keeping this
    /// behind an interface is what makes the machine testable: a test supplies
    /// a fake and drives input directly, with no desktop, no window handles and
    /// no injected keystrokes.
    /// </summary>
    public interface IPlatform
    {
        /// <summary>The current foreground top-level window, or IntPtr.Zero.</summary>
        IntPtr GetForegroundWindow();

        /// <summary>Whether a window handle belongs to Windows Terminal.</summary>
        bool IsTerminalWindow(IntPtr hwnd);

        /// <summary>
        /// Every input event observed since the previous call, and clears the
        /// queue. Returns an empty list when nothing happened, so the caller
        /// never has to reason about a missed or repeated event.
        /// </summary>
        List<InputEvent> DrainInput();

        /// <summary>
        /// Discards anything pending, for when the widget's own controls were
        /// clicked. Operating the widget is not terminal input.
        /// </summary>
        void DiscardPendingInput();

        /// <summary>
        /// Switch the terminal to its next tab, as Ctrl+Tab would. This is
        /// synthesised, so a hooked platform will see its own event and must
        /// mark it injected, which it does.
        /// </summary>
        void InjectNextTab();

        /// <summary>Whether input is being observed at full fidelity.</summary>
        bool HasHighFidelityInput { get; }
    }
}
