using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TabCycler
{
    /// <summary>
    /// The real Win32 side of <see cref="IPlatform"/>, plus a low-level input
    /// hook.
    ///
    /// The hook is the point. GetLastInputInfo reports only that input
    /// happened, so typing, clicking and scrolling are indistinguishable, and
    /// there is no way to tell a real hand from another process driving the
    /// mouse. A WH_KEYBOARD_LL / WH_MOUSE_LL hook gives the event kind and the
    /// INJECTED flag, which is what the per-kind toggles need and what stops
    /// synthetic input from reading as the user.
    ///
    /// If the hook cannot be installed, the platform falls back to the older
    /// timestamp-and-cursor heuristic so the tool still works, reporting
    /// <see cref="InputKind.Unclassified"/> and losing per-kind detail rather
    /// than losing function.
    /// </summary>
    public sealed class Win32Platform : IPlatform, IDisposable
    {
        // ---- low level hook constants -------------------------------------
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;

        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        const int WM_MOUSEMOVE = 0x0200;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MBUTTONDOWN = 0x0207;
        const int WM_XBUTTONDOWN = 0x020B;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_MOUSEHWHEEL = 0x020E;

        const uint LLKHF_INJECTED = 0x00000010;
        const uint LLMHF_INJECTED = 0x00000001;

        /// <summary>
        /// Cap on the queue. The hook fires for the whole system, so a long
        /// stretch where the terminal is not in front would otherwise pile up
        /// without bound. Events are only read while the terminal has focus, so
        /// keeping a window of the most recent is the right trade.
        /// </summary>
        const int MaxQueued = 256;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT_ { internal int X; internal int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            internal uint vkCode;
            internal uint scanCode;
            internal uint flags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            internal int X;
            internal int Y;
            internal uint mouseData;
            internal uint flags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        // The managed name differs from the IPlatform method of the same name,
        // so EntryPoint must be stated. Without it the runtime looks for an
        // export called NativeGetForegroundWindow, which does not exist, and
        // throws EntryPointNotFound on the first call. This exact mistake was
        // made twice in this file; PlatformTests exists to keep catching it.
        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        static extern IntPtr NativeGetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT_ lpPoint);

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(out LASTINPUTINFO plii);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            internal uint cbSize;
            internal uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            internal int dx, dy;
            internal uint mouseData, dwFlags, time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            internal ushort wVk, wScan;
            internal uint dwFlags, time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            internal uint uMsg;
            internal ushort wParamL, wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] internal MOUSEINPUT mi;
            [FieldOffset(0)] internal KEYBDINPUT ki;
            [FieldOffset(0)] internal HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            internal uint type;
            internal INPUTUNION u;
        }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_TAB = 0x09;

        // Kept alive deliberately: if the delegate is collected while the hook
        // is installed, the callback fires into freed memory.
        readonly HookProc _keyboardProc;
        readonly HookProc _mouseProc;

        IntPtr _keyboardHook = IntPtr.Zero;
        IntPtr _mouseHook = IntPtr.Zero;
        readonly List<InputEvent> _pending = new List<InputEvent>();
        bool _disposed;

        // Fallback path state, used only when the hook is unavailable.
        uint _lastStamp;
        bool _haveStamp;
        CursorPos _lastCursor;

        public bool HasHighFidelityInput { get { return _keyboardHook != IntPtr.Zero; } }

        public Win32Platform()
        {
            _keyboardProc = KeyboardCallback;
            _mouseProc = MouseCallback;

            IntPtr module = GetModuleHandle(null);
            if (module == IntPtr.Zero) module = IntPtr.Zero;

            try
            {
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
                if (_keyboardHook == IntPtr.Zero)
                    Debug.WriteLine("TabCycler: keyboard hook not installed, err=" + Marshal.GetLastWin32Error());

                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
                if (_mouseHook == IntPtr.Zero)
                    Debug.WriteLine("TabCycler: mouse hook not installed, err=" + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                // A hook is an enhancement. Never let it take the tool down.
                Debug.WriteLine("TabCycler: hook install failed, falling back: " + ex);
            }

            CaptureFallbackBaseline();
        }

        ~Win32Platform()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        // ---- hook callbacks -------------------------------------------------
        // These run on the thread that installed the hook, inside the message
        // pump. They must stay short and must never block or throw, or the
        // whole desktop feels sticky.

        IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
            {
                try
                {
                    KBDLLHOOKSTRUCT info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(KBDLLHOOKSTRUCT));
                    bool injected = (info.flags & LLKHF_INJECTED) != 0;
                    Enqueue(new InputEvent(InputKind.KeyPress, injected, CursorNow()));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("TabCycler: keyboard hook callback failed: " + ex);
                }
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(MSLLHOOKSTRUCT));
                    bool injected = (info.flags & LLMHF_INJECTED) != 0;
                    CursorPos pos = new CursorPos(info.X, info.Y);

                    InputKind kind;
                    switch (msg)
                    {
                        case WM_MOUSEMOVE: kind = InputKind.Movement; break;
                        case WM_LBUTTONDOWN:
                        case WM_RBUTTONDOWN:
                        case WM_MBUTTONDOWN:
                        case WM_XBUTTONDOWN: kind = InputKind.MouseClick; break;
                        case WM_MOUSEWHEEL:
                        case WM_MOUSEHWHEEL: kind = InputKind.Scroll; break;
                        default: kind = InputKind.None; break;
                    }
                    if (kind != InputKind.None) Enqueue(new InputEvent(kind, injected, pos));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("TabCycler: mouse hook callback failed: " + ex);
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        void Enqueue(InputEvent evt)
        {
            if (_pending.Count >= MaxQueued) _pending.RemoveAt(0);
            _pending.Add(evt);
        }

        // ---- IPlatform -------------------------------------------------------

        public IntPtr GetForegroundWindow()
        {
            return NativeGetForegroundWindow();
        }

        public bool IsTerminalWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            try
            {
                // Identified by owning process rather than window class, so it
                // keeps working if Windows Terminal ever renames
                // CASCADIA_HOSTING_WINDOW_CLASS.
                string name = Process.GetProcessById((int)pid).ProcessName;
                return name.StartsWith("WindowsTerminal", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // The process can exit between the window check and this lookup.
                Debug.WriteLine("TabCycler: process lookup failed: " + ex);
                return false;
            }
        }

        public List<InputEvent> DrainInput()
        {
            if (HasHighFidelityInput)
            {
                if (_pending.Count == 0) return new List<InputEvent>();
                List<InputEvent> drained = new List<InputEvent>(_pending);
                _pending.Clear();
                return drained;
            }
            return DrainFallback();
        }

        public void DiscardPendingInput()
        {
            if (HasHighFidelityInput) _pending.Clear();
            else _haveStamp = false;   // force the fallback to re-baseline
        }

        /// <summary>
        /// Ctrl+Tab as one atomic batch, so no window can observe a bare Tab
        /// that a shell would read as an indent character. The hook will see
        /// the result and mark it injected, so it is ignored like any other
        /// synthesised input rather than needing to be matched by hand.
        /// </summary>
        public void InjectNextTab()
        {
            int size = Marshal.SizeOf(typeof(INPUT));
            INPUT[] batch = new INPUT[4];
            batch[0] = Key(VK_CONTROL, 0);
            batch[1] = Key(VK_TAB, 0);
            batch[2] = Key(VK_TAB, KEYEVENTF_KEYUP);
            batch[3] = Key(VK_CONTROL, KEYEVENTF_KEYUP);

            uint sent = SendInput(4, batch, size);
            if (sent != 4)
                Debug.WriteLine("TabCycler: SendInput sent " + sent + " of 4, err=" +
                                Marshal.GetLastWin32Error());
        }

        // ---- fallback: no hook, so less detail ------------------------------

        void CaptureFallbackBaseline()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (GetLastInputInfo(out lii)) _lastStamp = lii.dwTime;
            _haveStamp = true;
            _lastCursor = CursorNow();
        }

        List<InputEvent> DrainFallback()
        {
            List<InputEvent> found = new List<InputEvent>();

            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            bool ok = GetLastInputInfo(out lii);
            if (!ok)
            {
                Debug.WriteLine("TabCycler: GetLastInputInfo failed, err=" + Marshal.GetLastWin32Error());
                return found;
            }

            CursorPos cursor = CursorNow();
            bool isNew = !_haveStamp || lii.dwTime != _lastStamp;
            // Compare against the previous sample, so the update below cannot
            // make this test trivially true.
            int dx = Math.Abs(cursor.X - _lastCursor.X);
            int dy = Math.Abs(cursor.Y - _lastCursor.Y);

            _lastStamp = lii.dwTime;
            _haveStamp = true;
            _lastCursor = cursor;

            if (!isNew) return found;

            // Without a hook there is no way to know which of the three it was,
            // and no way to know whether it was synthetic at all. Movement is
            // still separable, because the pointer position is real.
            found.Add((dx == 0 && dy == 0)
                ? new InputEvent(InputKind.Unclassified, false, cursor)
                : new InputEvent(InputKind.Movement, false, cursor));
            return found;
        }

        CursorPos CursorNow()
        {
            POINT_ p;
            if (!GetCursorPos(out p))
            {
                Debug.WriteLine("TabCycler: GetCursorPos failed, err=" + Marshal.GetLastWin32Error());
                return new CursorPos(0, 0);
            }
            return new CursorPos(p.X, p.Y);
        }

        static INPUT Key(ushort vk, uint flags)
        {
            INPUT i = new INPUT();
            i.type = INPUT_KEYBOARD;
            i.u.ki.wVk = vk;
            i.u.ki.wScan = 0;
            i.u.ki.dwFlags = flags;
            i.u.ki.time = 0;
            i.u.ki.dwExtraInfo = IntPtr.Zero;
            return i;
        }
    }
}
