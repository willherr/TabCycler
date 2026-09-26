// TabCycler: a small always-on-top widget that rotates the active Windows
// Terminal tab on an interval so you can watch several agents work without
// clicking. Pause/resume from the widget, X on the widget to stop it.
//
// Build: pwsh -NoProfile -File .\build.ps1
// Source of truth for the two timings is %LOCALAPPDATA%\TabCycler\settings.txt
// (IntervalSeconds / ResumeDelaySeconds), so they are tweakable without a rebuild.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TabCycler
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CyclerForm());
        }
    }

    /// <summary>Reads/writes the small key=value settings file, no dependencies.</summary>
    internal sealed class Settings
    {
        public int IntervalSeconds = 5;
        public int ResumeDelaySeconds = 60;
        public int Left, Top;
        public bool SeenLeft, SeenTop;

        private static string Path_
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabCycler");
                return System.IO.Path.Combine(dir, "settings.txt");
            }
        }

        public Settings()
        {
            try
            {
                if (!File.Exists(Path_)) return;
                foreach (string raw in File.ReadAllLines(Path_))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int n;
                    switch (k.ToLowerInvariant())
                    {
                        case "intervalseconds":
                            if (int.TryParse(v, out n) && n >= 1) IntervalSeconds = n;
                            break;
                        case "resumedelayseconds":
                            if (int.TryParse(v, out n) && n >= 0) ResumeDelaySeconds = n;
                            break;
                        case "left":
                            if (int.TryParse(v, out n)) { Left = n; SeenLeft = true; }
                            break;
                        case "top":
                            if (int.TryParse(v, out n)) { Top = n; SeenTop = true; }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings read failed, using defaults: " + ex);
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# TabCycler settings");
                sb.AppendLine("IntervalSeconds=" + IntervalSeconds);
                sb.AppendLine("ResumeDelaySeconds=" + ResumeDelaySeconds);
                sb.AppendLine("Left=" + Left);
                sb.AppendLine("Top=" + Top);
                File.WriteAllText(Path_, sb.ToString());
            }
            catch (Exception ex)
            {
                // Losing the widget position is not worth interrupting the user
                // over, but do not swallow it silently either.
                Debug.WriteLine("TabCycler: settings write failed: " + ex);
            }
        }
    }

    /// <summary>
    /// One state machine, one source of truth. Paused used to be a separate
    /// boolean alongside these states, and later a fourth state of its own,
    /// which meant "stopped" had two different spellings and the button label
    /// could disagree with what a click actually did. Stopped is now just
    /// Holding: pausing and going idle after a return are the same situation,
    /// and Start is the single way out of it.
    /// </summary>
    internal enum WatchState
    {
        /// <summary>Windows Terminal does not have focus: the user is elsewhere.</summary>
        Away,
        /// <summary>
        /// Not cycling. Waiting out the resume delay. Reached by pausing, by
        /// user input while cycling, and by returning to the terminal.
        /// </summary>
        Holding,
        /// <summary>Normal: cycle on the interval.</summary>
        Cycling
    }

    internal sealed class CyclerForm : Form
    {
        // ---- palette -------------------------------------------------------
        static readonly Color Back = Color.FromArgb(0x1F, 0x1F, 0x1F);
        static readonly Color Edge = Color.FromArgb(0x44, 0x44, 0x44);
        static readonly Color Title = Color.FromArgb(0xEC, 0xEC, 0xEC);
        static readonly Color Body = Color.FromArgb(0xA6, 0xA6, 0xA6);
        static readonly Color BtnFace = Color.FromArgb(0x33, 0x33, 0x33);
        static readonly Color BtnHover = Color.FromArgb(0x44, 0x44, 0x44);
        static readonly Color StartFace = Color.FromArgb(0x4A, 0x4A, 0x4A);
        static readonly Color Danger = Color.FromArgb(0xC4, 0x2B, 0x1C);

        // Widget geometry below is authored at 96 DPI and multiplied by the
        // display scale, because the app is per-monitor DPI aware: fonts are
        // sized in points so they grow with the DPI on their own, but control
        // bounds are in pixels and would not, which squashes the text.
        const int W = 320, H = 82;

        readonly Settings _cfg;
        readonly float _scale;
        readonly Timer _tick = new Timer();
        readonly Label _title = new Label();
        readonly Label _status = new Label();
        readonly Label _detail = new Label();
        readonly Button _toggle = new Button();
        readonly Button _close = new Button();

        int Px(int v) { return (int)Math.Round(v * _scale, MidpointRounding.AwayFromZero); }

        WatchState _state = WatchState.Holding;
        DateTime _resumeAt;
        DateTime _nextCycleAt = DateTime.MinValue;
        uint _lastInjectionTick;
        Point _lastCursor;

        static string LogPath
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabCycler");
                return System.IO.Path.Combine(dir, "tabcycler.log");
            }
        }

        /// <summary>
        /// Appends a timestamped line so the state machine's decisions can be
        /// inspected after the fact instead of guessed at from the UI.
        /// </summary>
        void Log(string message)
        {
            try
            {
                string path = LogPath;
                // Cycles log a line every few seconds, so an all-day run adds up.
                // Start a fresh file rather than let it grow without bound.
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                {
                    File.Delete(path);
                    File.AppendAllText(path, "--- log truncated (1MB) " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ---" + Environment.NewLine);
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    "  " + message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: log write failed: " + ex);
            }
        }

        public CyclerForm()
        {
            _cfg = new Settings();

            // Read the real display scale before laying anything out. On a 150%
            // display this is 1.5, so a 96-DPI layout is multiplied out to real
            // pixels that match the point-sized fonts.
            float dpi = 96f;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
            {
                if (screen != null && screen.DpiX > 0) dpi = screen.DpiX;
            }
            _scale = dpi / 96f;

            SuspendLayout();
            Text = "Tab Cycler";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Px(W), Px(H));
            BackColor = Back;
            ForeColor = Body;
            TopMost = true;                 // stay above the terminal and everything else
            ShowInTaskbar = true;           // own taskbar button, pinnable
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;   // scaling is done explicitly by Px()

            _title.Text = "Tab Cycler";
            _title.ForeColor = Title;
            _title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _title.BackColor = Color.Transparent;
            _title.Location = new Point(Px(10), Px(8));
            _title.Size = new Size(Px(185), Px(18));
            _title.TextAlign = ContentAlignment.MiddleLeft;

            _status.Font = new Font("Segoe UI", 8.5f);
            _status.ForeColor = Body;
            _status.BackColor = Color.Transparent;
            _status.Location = new Point(Px(10), Px(34));
            _status.Size = new Size(Px(300), Px(17));
            _status.TextAlign = ContentAlignment.MiddleLeft;

            _detail.Font = new Font("Segoe UI", 8.5f);
            _detail.ForeColor = Color.FromArgb(0x7A, 0x7A, 0x7A);
            _detail.BackColor = Color.Transparent;
            _detail.Location = new Point(Px(10), Px(56));
            _detail.Size = new Size(Px(300), Px(17));
            _detail.TextAlign = ContentAlignment.MiddleLeft;

            ConfigureButton(_toggle, new Point(Px(202), Px(5)), new Size(Px(72), Px(26)), "Start");
            ConfigureButton(_close, new Point(Px(280), Px(5)), new Size(Px(30), Px(26)), "X");
            _close.ForeColor = Color.FromArgb(0xC9, 0xC9, 0xC9);
            _close.MouseEnter += delegate { _close.BackColor = Danger; _close.ForeColor = Color.White; };
            _close.MouseLeave += delegate { _close.BackColor = BtnFace; _close.ForeColor = Color.FromArgb(0xC9, 0xC9, 0xC9); };
            _toggle.MouseEnter += delegate { if (_toggle.BackColor == BtnFace) _toggle.BackColor = BtnHover; };
            _toggle.MouseLeave += delegate { if (_toggle.BackColor == BtnFace) _toggle.BackColor = BtnFace; };

            _toggle.Click += delegate { OnToggleClick(); };
            _close.Click += delegate { CloseAndExit(); };

            Controls.AddRange(new Control[] { _title, _status, _detail, _toggle, _close });
            ResumeLayout(true);

            // Drag anywhere on the (non-button) body to move the widget.
            MouseDown += OnDragStart;
            foreach (Control c in new Control[] { _title, _status, _detail })
            {
                c.MouseDown += OnDragStart;
            }

            RestorePosition();

            // Persist the position shortly after a drag settles, not just on
            // exit, so closing the widget some other way does not lose it.
            DateTime lastSave = DateTime.MinValue;
            Point lastPos = new Point(int.MinValue, int.MinValue);
            Move += delegate
            {
                if (Left == lastPos.X && Top == lastPos.Y) return;
                lastPos = new Point(Left, Top);
                if ((DateTime.Now - lastSave).TotalMilliseconds < 750) return;
                lastSave = DateTime.Now;
                SavePosition();
            };

            Log("started: interval=" + _cfg.IntervalSeconds + "s resumeDelay=" +
                _cfg.ResumeDelaySeconds + "s at " + Left + "," + Top);

            // Start out holding, and treat whatever the user last did before
            // launch as already-consumed so it does not immediately re-trigger.
            _resumeAt = DateTime.Now.AddSeconds(_cfg.ResumeDelaySeconds);
            _lastInjectionTick = Native.LastInputTick();
            _lastCursor = Native.CursorPosition();

            _tick.Interval = 250;
            _tick.Tick += delegate { Tick(); };
            _tick.Start();

            FormClosing += delegate { SavePosition(); _tick.Stop(); };
        }

        void ConfigureButton(Button b, Point loc, Size size, string text)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = BtnHover;
            b.BackColor = BtnFace;
            b.ForeColor = Color.FromArgb(0xDE, 0xDE, 0xDE);
            b.Font = new Font("Segoe UI", 8.5f);
            b.Location = loc;
            b.Size = size;
            b.Text = text;
            b.UseVisualStyleBackColor = false;
        }

        void OnDragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Dragging the widget is operating the widget, not the terminal.
            ConsumeOwnInput();
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
        }

        void RestorePosition()
        {
            Screen screen = Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            int fw = Px(W), fh = Px(H);
            if (_cfg.SeenLeft && _cfg.SeenTop)
            {
                // Only trust a saved position that still lands on some display,
                // otherwise a resolution change would park it offscreen forever.
                bool visible = false;
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(new Rectangle(_cfg.Left, _cfg.Top, fw, fh)))
                    {
                        visible = true;
                        screen = s;
                        break;
                    }
                }
                if (visible)
                {
                    Left = _cfg.Left;
                    Top = _cfg.Top;
                    return;
                }
            }
            // Default: top-right of the primary screen, clear of the tab bar.
            Left = wa.Right - fw - Px(16);
            Top = wa.Top + Px(48);
        }

        void SavePosition()
        {
            _cfg.Left = Left;
            _cfg.Top = Top;
            _cfg.Save();
        }

        /// <summary>
        /// Marks the current last-input stamp as already handled. Pressing a
        /// widget button is a real click, so without this the very next poll
        /// reads it as the user taking over in the terminal and re-arms the
        /// hold, bouncing straight back to "Holding" the instant Start is
        /// pressed. Operating the widget is not terminal input.
        /// </summary>
        void ConsumeOwnInput()
        {
            _lastInjectionTick = Native.LastInputTick();
        }

        /// <summary>
        /// One contextual button, and its meaning is decided purely by the
        /// current state, which is the same value the label was drawn from.
        /// While cycling it offers "Pause", which drops into Holding for the
        /// full resume delay; in Holding it offers "Start", which skips it.
        /// </summary>
        void OnToggleClick()
        {
            if (_state == WatchState.Cycling)
            {
                // Pause is not its own state: it is the same 60s hold that
                // follows a return or a keystroke, so there is only one way to
                // be stopped and one way out of it.
                _state = WatchState.Holding;
                _resumeAt = DateTime.Now.AddSeconds(_cfg.ResumeDelaySeconds);
                Log("pause pressed -> holding for " + _cfg.ResumeDelaySeconds + "s");
            }
            else
            {
                _state = WatchState.Cycling;
                // Skips any remaining hold, but still waits one full interval
                // before the first switch so the tab you just chose is readable.
                _resumeAt = DateTime.MinValue;
                _nextCycleAt = DateTime.Now.AddSeconds(_cfg.IntervalSeconds);
                Log("start pressed -> first tab in " + _cfg.IntervalSeconds +
                    "s, then every " + _cfg.IntervalSeconds + "s");
            }
            ConsumeOwnInput();
            UpdateStatus();
        }

        void CloseAndExit()
        {
            Log("close button pressed, exiting");
            ConsumeOwnInput();
            SavePosition();
            _tick.Stop();
            Application.Exit();
        }

        /// <summary>
        /// Decides whether the user did something deliberate since the last poll,
        /// and if so pushes the hold out again.
        ///
        /// Pointer movement is deliberately excluded. GetLastInputInfo cannot
        /// tell input kinds apart, and it does record movement, so a drifting
        /// mouse re-armed the hold on every 250ms poll: the countdown never got
        /// past 60 and the widget looked stuck. A key press, click or scroll does
        /// not move the pointer, so "the stamp changed but the cursor is in the
        /// same place" is what distinguishes a real action from movement.
        /// </summary>
        bool ConsumeUserInput(DateTime now)
        {
            uint tick = Native.LastInputTick();
            if (tick == _lastInjectionTick) return false;

            Point cursor = Native.CursorPosition();
            bool cursorMoved = cursor.X != _lastCursor.X || cursor.Y != _lastCursor.Y;
            _lastCursor = cursor;

            // Consume the stamp either way, so the same event is not re-examined
            // on the next poll.
            _lastInjectionTick = tick;
            if (cursorMoved) return false;

            _resumeAt = now.AddSeconds(_cfg.ResumeDelaySeconds);
            Log("input -> holding " + _cfg.ResumeDelaySeconds + "s");
            return true;
        }

        void Tick()
        {
            DateTime now = DateTime.Now;
            IntPtr fg = Native.GetForegroundWindow();

            // The widget has focus: the user is clicking Start/Pause/X, not
            // watching. Hold state as-is so operating the widget never counts
            // as "leaving" and never restarts the hold timer.
            if (fg == Handle) { UpdateStatus(); return; }

            bool watching = Native.IsWindowsTerminalWindow(fg);

            if (!watching)
            {
                if (_state != WatchState.Away)
                {
                    _state = WatchState.Away;
                    _nextCycleAt = DateTime.MinValue;
                    Log("left Windows Terminal -> Idle");
                }
            }
            else
            {
                switch (_state)
                {
                    case WatchState.Away:
                        // Focus arrived at the terminal. If a click caused it the
                        // input check below already restarted the hold; if focus
                        // arrived on its own, start one here.
                        if (ConsumeUserInput(now) || _resumeAt <= now)
                            _resumeAt = now.AddSeconds(_cfg.ResumeDelaySeconds);
                        _state = WatchState.Holding;
                        Log("terminal focused -> holding " + _cfg.ResumeDelaySeconds + "s");
                        break;

                    case WatchState.Holding:
                        if (ConsumeUserInput(now))
                        {
                            _resumeAt = now.AddSeconds(_cfg.ResumeDelaySeconds);
                        }
                        else if (now >= _resumeAt)
                        {
                            _state = WatchState.Cycling;
                            // Give one full interval before the first switch, so
                            // whatever the user just looked at stays readable.
                            _nextCycleAt = now.AddSeconds(_cfg.IntervalSeconds);
                            Log("hold elapsed -> cycling every " + _cfg.IntervalSeconds + "s");
                        }
                        break;

                    case WatchState.Cycling:
                        if (ConsumeUserInput(now))
                        {
                            _state = WatchState.Holding;
                            _resumeAt = now.AddSeconds(_cfg.ResumeDelaySeconds);
                        }
                        else if (now >= _nextCycleAt)
                        {
                            // Re-check focus immediately before injecting keys: the
                            // keystroke goes to whatever is foreground at that instant,
                            // so narrow the gap between the check and the send.
                            if (Native.IsWindowsTerminalWindow(Native.GetForegroundWindow()))
                            {
                                Native.SendCtrlTab();
                                // Record the stamp our own keystroke just produced, so it
                                // is not read back as the user having taken over.
                                _lastInjectionTick = Native.LastInputTick();
                                _nextCycleAt = now.AddSeconds(_cfg.IntervalSeconds);
                                Log("cycle -> sent Ctrl+Tab");
                            }
                            else
                            {
                                Log("focus changed mid-send, backing off -> Idle");
                                _state = WatchState.Away;
                            }
                        }
                        break;
                }
            }

            UpdateStatus();
        }

        void UpdateStatus()
        {
            // Derived from the same _state the click handler reads, so the label
            // can never promise one action while the handler does another.
            bool cycling = (_state == WatchState.Cycling);
            string wantLabel = cycling ? "Pause" : "Start";
            if (_toggle.Text != wantLabel)
            {
                _toggle.Text = wantLabel;
                _toggle.BackColor = cycling ? BtnFace : StartFace;
                _toggle.ForeColor = cycling ? Color.FromArgb(0xDE, 0xDE, 0xDE) : Color.White;
            }

            _status.ForeColor = Body;
            switch (_state)
            {
                case WatchState.Away:
                    _status.Text = "Idle";
                    _detail.Text = "focus Windows Terminal to start";
                    break;
                case WatchState.Holding:
                    int left = (int)Math.Ceiling((_resumeAt - DateTime.Now).TotalSeconds);
                    if (left < 0) left = 0;
                    _status.Text = "Holding for " + left + "s";
                    _detail.Text = "reset by your typing or clicks";
                    break;
                default:
                    _status.Text = "Cycling every " + _cfg.IntervalSeconds + "s";
                    int nxt = (int)Math.Ceiling((_nextCycleAt - DateTime.Now).TotalSeconds);
                    if (nxt < 0) nxt = 0;
                    _detail.Text = "next tab in " + nxt + "s";
                    break;
            }
        }

        /// <summary>
        /// Launch the widget without taking focus. A WinForms form grabs focus
        /// by default, which here is self-defeating: the cycler keys off what
        /// has foreground, so a focused widget looks like "user is elsewhere" and
        /// the whole thing sits Idle until the terminal is clicked.
        /// </summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_NOACTIVATE: clicking the buttons still works, the window
                // just never pulls focus away from the terminal.
                cp.ExStyle |= 0x08000000;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Edge))
            {
                e.Graphics.DrawRectangle(p, 0, 0, Px(W) - 1, Px(H) - 1);
            }
        }
    }

    internal static class Native
    {
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int HTCAPTION = 0x0002;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            internal int X;
            internal int Y;
        }

        /// <summary>
        /// Current pointer position, used to tell a deliberate key press or
        /// click apart from the pointer simply drifting.
        /// </summary>
        internal static Point CursorPosition()
        {
            POINT p;
            if (!GetCursorPos(out p))
            {
                Debug.WriteLine("TabCycler: GetCursorPos failed, err=" + Marshal.GetLastWin32Error());
                return new Point(0, 0);
            }
            return new Point(p.X, p.Y);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            internal uint cbSize;
            internal uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(out LASTINPUTINFO plii);

        /// <summary>
        /// Tick stamp of the most recent keyboard or mouse input anywhere on
        /// the system. Compared against the stamp taken right after each
        /// injected Ctrl+Tab, so our own keystrokes are not mistaken for the
        /// user taking over.
        /// </summary>
        internal static uint LastInputTick()
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

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            internal int dx, dy;
            internal uint mouseData, dwFlags, time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            internal ushort wVk, wScan;
            internal uint dwFlags, time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            internal uint uMsg;
            internal ushort wParamL, wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] internal MOUSEINPUT mi;
            [FieldOffset(0)] internal KEYBDINPUT ki;
            [FieldOffset(0)] internal HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            internal uint type;
            internal INPUTUNION u;
        }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_TAB = 0x09;

        /// <summary>
        /// True when the given top-level window belongs to Windows Terminal.
        /// Identified by owning process rather than window class, so it keeps
        /// working across WT releases that rename CASCADIA_HOSTING_WINDOW_CLASS.
        /// </summary>
        internal static bool IsWindowsTerminalWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == 0) return false;
            try
            {
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

        /// <summary>
        /// Injects Ctrl+Tab as one atomic SendInput batch so no window can ever
        /// observe a bare Tab (which a shell would read as an indent character).
        /// </summary>
        internal static void SendCtrlTab()
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
