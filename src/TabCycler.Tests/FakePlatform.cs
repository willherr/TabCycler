using System;
using System.Collections.Generic;

namespace TabCycler.Tests
{
    /// <summary>
    /// Stands in for the real desktop. Tests drive focus, input stamps and
    /// pointer position directly, and read back how many keystrokes the engine
    /// injected, so the whole machine can be exercised with no window and no
    /// synthetic input.
    /// </summary>
    public sealed class FakePlatform : IPlatform
    {
        /// <summary>Foreground window handle the fake reports.</summary>
        public IntPtr Foreground = new IntPtr(1000);

        /// <summary>Whether that foreground window is Windows Terminal.</summary>
        public bool TerminalInFront = true;

        /// <summary>Current system input stamp.</summary>
        public uint Stamp = 500;

        public int CursorX = 800, CursorY = 400;

        public int Injections { get; private set; }
        public List<string> Messages { get; private set; }

        public FakePlatform()
        {
            Messages = new List<string>();
        }

        public IntPtr GetForegroundWindow() { return Foreground; }

        public bool IsTerminalWindow(IntPtr hwnd)
        {
            return TerminalInFront && hwnd != IntPtr.Zero;
        }

        public uint LastInputStamp() { return Stamp; }

        public CursorPos CursorPosition() { return new CursorPos(CursorX, CursorY); }

        public void InjectNextTab()
        {
            Injections++;
            // A real SendInput bumps the system input stamp, and the engine has
            // to cope with that, so the fake reproduces it.
            Stamp = Stamp + 1000;
        }

        // ---- test-side controls -------------------------------------------

        /// <summary>Simulate the user pressing a key, pointer unmoved.</summary>
        public void TypeKey() { Stamp = Stamp + 1; }

        /// <summary>Simulate a click, pointer unmoved.</summary>
        public void Click() { Stamp = Stamp + 1; }

        /// <summary>
        /// A twitch of the pointer, as a resting hand produces. Moves the
        /// cursor by a couple of pixels and bumps the input stamp, which is
        /// what real movement does.
        /// </summary>
        public void DriftPointer() { MovePointer(2, 1); }

        /// <summary>Move the pointer an explicit amount, in either direction.</summary>
        public void MovePointer(int dx, int dy)
        {
            CursorX = CursorX + dx;
            CursorY = CursorY + dy;
            Stamp = Stamp + 1;
        }

        /// <summary>
        /// A wheel event. It changes the input stamp but does not move the
        /// pointer, which is exactly why it is already detected.
        /// </summary>
        public void ScrollWheel() { Stamp = Stamp + 1; }

        /// <summary>Click the widget, which also moves the pointer onto it.</summary>
        public void ClickWidget(IntPtr widgetHandle)
        {
            Foreground = new IntPtr(2000);   // some other window has focus
            TerminalInFront = false;
            Click();
            Foreground = new IntPtr(1000);   // focus never actually moved
            TerminalInFront = true;
        }

        public bool SawMessage(string fragment)
        {
            foreach (string m in Messages) if (m.Contains(fragment)) return true;
            return false;
        }
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
