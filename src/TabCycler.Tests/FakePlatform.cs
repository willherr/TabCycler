using System;
using System.Collections.Generic;

namespace TabCycler.Tests
{
    /// <summary>
    /// Stands in for the real desktop. Tests push typed input events and read
    /// back how many keystrokes the engine injected, so the whole machine can be
    /// exercised with no window, no hook and no synthetic input.
    /// </summary>
    public sealed class FakePlatform : IPlatform
    {
        /// <summary>Foreground window handle the fake reports.</summary>
        public IntPtr Foreground = new IntPtr(1000);

        /// <summary>Whether that foreground window is Windows Terminal.</summary>
        public bool TerminalInFront = true;

        /// <summary>Where the pointer is, so movement distance means something.</summary>
        public int CursorX = 800, CursorY = 400;

        public int Injections { get; private set; }

        readonly List<InputEvent> _pending = new List<InputEvent>();

        public bool HasHighFidelityInput { get { return true; } }

        public IntPtr GetForegroundWindow() { return Foreground; }

        public bool IsTerminalWindow(IntPtr hwnd)
        {
            return TerminalInFront && hwnd != IntPtr.Zero;
        }

        public List<InputEvent> DrainInput()
        {
            List<InputEvent> out_ = new List<InputEvent>(_pending);
            _pending.Clear();
            return out_;
        }

        public void DiscardPendingInput() { _pending.Clear(); }

        public void InjectNextTab()
        {
            Injections++;
            // A real SendInput shows up in the hook flagged as injected, so the
            // fake reproduces that. The engine must not read it as the user.
            Push(new InputEvent(InputKind.KeyPress, true, new CursorPos(CursorX, CursorY)));
        }

        // ---- test-side controls -------------------------------------------

        void Push(InputEvent e) { _pending.Add(e); }

        /// <summary>The user pressed a key.</summary>
        public void TypeKey() { Push(new InputEvent(InputKind.KeyPress, false, Pos())); }

        /// <summary>The user clicked, without moving the pointer.</summary>
        public void Click() { Push(new InputEvent(InputKind.MouseClick, false, Pos())); }

        /// <summary>A wheel event, which does not move the pointer.</summary>
        public void ScrollWheel() { Push(new InputEvent(InputKind.Scroll, false, Pos())); }

        /// <summary>
        /// What the fallback path reports when there is no hook: known to be one
        /// of key, click or scroll, but not which.
        /// </summary>
        public void PushUnclassified() { Push(new InputEvent(InputKind.Unclassified, false, Pos())); }

        /// <summary>Synthesised input, as another process driving the machine.</summary>
        public void InjectedKey() { Push(new InputEvent(InputKind.KeyPress, true, Pos())); }

        public void InjectedMovement(int dx, int dy) { MovePointer(dx, dy, true); }

        /// <summary>
        /// A twitch of the pointer, as a resting hand produces: a couple of
        /// pixels, the kind of thing that must not hold the widget off.
        /// </summary>
        public void DriftPointer() { MovePointer(2, 1); }

        /// <summary>
        /// The pointer moved. A hooked mouse emits one WM_MOUSEMOVE per sample,
        /// so a single call is one small step rather than one sweep.
        /// </summary>
        public void MovePointer(int dx, int dy) { MovePointer(dx, dy, false); }

        void MovePointer(int dx, int dy, bool injected)
        {
            CursorX += dx;
            CursorY += dy;
            Push(new InputEvent(InputKind.Movement, injected, Pos()));
        }

        /// <summary>A whole sweep, as the hook would report it.</summary>
        public void Sweep(int totalX, int totalY, int steps)
        {
            if (steps < 1) steps = 1;
            int sx = totalX / steps, sy = totalY / steps;
            for (int i = 0; i < steps; i++) MovePointer(sx, sy);
        }

        CursorPos Pos() { return new CursorPos(CursorX, CursorY); }
    }

    /// <summary>Collects the engine's log output for assertions.</summary>
    public sealed class LogSink
    {
        public readonly List<string> Lines = new List<string>();
        public void Add(string s) { Lines.Add(s); }
        public bool Saw(string fragment)
        {
            foreach (string l in Lines) if (l.Contains(fragment)) return true;
            return false;
        }
    }
}
