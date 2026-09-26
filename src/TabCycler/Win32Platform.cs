using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TabCycler
{
    /// <summary>
    /// The real Win32 side of <see cref="IPlatform"/>. Kept as thin as possible
    /// so the logic in CyclerEngine stays free of it and can be tested without
    /// a desktop.
    /// </summary>
    public sealed class Win32Platform : IPlatform
    {
        [DllImport("user32.dll")]
        static extern IntPtr NativeGetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(out LASTINPUTINFO plii);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { internal int X; internal int Y; }

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

        public uint LastInputStamp()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(out lii))
            {
                Debug.WriteLine("TabCycler: GetLastInputInfo failed, err=" + Marshal.GetLastWin32Error());
                return 0;
            }
            return lii.dwTime;
        }

        public CursorPos CursorPosition()
        {
            POINT p;
            if (!GetCursorPos(out p))
            {
                Debug.WriteLine("TabCycler: GetCursorPos failed, err=" + Marshal.GetLastWin32Error());
                return new CursorPos(0, 0);
            }
            return new CursorPos(p.X, p.Y);
        }

        /// <summary>
        /// Ctrl+Tab as one atomic batch, so no window can observe a bare Tab
        /// that a shell would read as an indent character.
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
