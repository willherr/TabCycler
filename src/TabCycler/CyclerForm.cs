using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabCycler
{
    /// <summary>
    /// The widget. This class only paints what <see cref="CyclerEngine"/>
    /// reports and forwards clicks and ticks to it. All the decisions live in
    /// the engine, which is why they can be tested without a window.
    /// </summary>
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

        // Geometry is authored at 96 DPI and multiplied out at runtime. See
        // ApplyDpi for why that is not simply done once in the constructor.
        const int W = 320, H = 82;

        readonly Settings _cfg;
        readonly CyclerEngine _engine;
        readonly Timer _tick = new Timer();
        readonly Label _title = new Label();
        readonly Label _status = new Label();
        readonly Label _detail = new Label();
        readonly Button _toggle = new Button();
        readonly Button _close = new Button();

        float _scale = 1f;
        bool _positioned;

        static string LogPath { get { return Path.Combine(Settings.Dir, "tabcycler.log"); } }

        public CyclerForm(IPlatform platform)
        {
            _cfg = new Settings();

            _engine = new CyclerEngine(platform, _cfg.IntervalSeconds, _cfg.ResumeDelaySeconds,
                                       DateTime.Now, _cfg.MoveThresholdPixels)
            {
                Log = Log
            };

            SuspendLayout();
            Text = "Tab Cycler";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Back;
            ForeColor = Body;
            TopMost = true;                 // stay above the terminal and everything else
            ShowInTaskbar = true;           // own taskbar button, pinnable
            MinimizeBox = false;
            MaximizeBox = false;
            // Deliberately None. WinForms' own DPI autoscaling reads the DPI via
            // CreateGraphics(), which reports 96 on a 150% display, so it
            // silently declines to scale. ApplyDpi does it properly instead.
            AutoScaleMode = AutoScaleMode.None;

            _title.Text = "Tab Cycler";
            _title.ForeColor = Title;
            _title.BackColor = Color.Transparent;
            _title.TextAlign = ContentAlignment.MiddleLeft;

            _status.ForeColor = Body;
            _status.BackColor = Color.Transparent;
            _status.TextAlign = ContentAlignment.MiddleLeft;

            _detail.ForeColor = Color.FromArgb(0x7A, 0x7A, 0x7A);
            _detail.BackColor = Color.Transparent;
            _detail.TextAlign = ContentAlignment.MiddleLeft;

            ConfigureButton(_toggle);
            ConfigureButton(_close);
            _close.Text = "X";
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
                c.MouseDown += OnDragStart;

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

            _tick.Interval = 250;
            _tick.Tick += delegate { OnPoll(); };
            _tick.Start();

            FormClosing += delegate { SavePosition(); _tick.Stop(); };
        }

        void ConfigureButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = BtnHover;
            b.BackColor = BtnFace;
            b.ForeColor = Color.FromArgb(0xDE, 0xDE, 0xDE);
            b.UseVisualStyleBackColor = false;
        }

        int P(int v) { return (int)Math.Round(v * _scale, MidpointRounding.AwayFromZero); }

        /// <summary>
        /// Lays the widget out for the DPI of the monitor it is actually on.
        ///
        /// This has to run after the window handle exists, because the only
        /// trustworthy DPI source is GetDpiForWindow. Reading it in the
        /// constructor produced 96 on a 150% display, and so did
        /// AutoScaleMode.Dpi, which uses CreateGraphics() internally. The
        /// symptom was a 320x82 box holding 150%-scaled fonts, so the buttons
        /// jammed against the edge and the hint text was clipped.
        /// </summary>
        void ApplyDpi()
        {
            uint dpi = Native.GetDpiForWindow(Handle);
            _scale = (dpi > 0 ? dpi : 96u) / 96f;

            ClientSize = new Size(P(W), P(H));

            _title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _title.Location = new Point(P(10), P(8));
            _title.Size = new Size(P(185), P(18));

            _status.Font = new Font("Segoe UI", 8.5f);
            _status.Location = new Point(P(10), P(34));
            _status.Size = new Size(P(300), P(17));

            _detail.Font = new Font("Segoe UI", 8.5f);
            _detail.Location = new Point(P(10), P(56));
            _detail.Size = new Size(P(300), P(17));

            _toggle.Font = new Font("Segoe UI", 8.5f);
            _toggle.Location = new Point(P(202), P(5));
            _toggle.Size = new Size(P(72), P(26));

            _close.Font = new Font("Segoe UI", 8.5f);
            _close.Location = new Point(P(280), P(5));
            _close.Size = new Size(P(30), P(26));

            Invalidate();
        }

        void OnPoll()
        {
            _engine.Tick(DateTime.Now);
            UpdateLabels();
        }

        void OnToggleClick()
        {
            _engine.Toggle(DateTime.Now);
            // Pressing the widget is not terminal input.
            _engine.NoteOwnInput();
            UpdateLabels();
        }

        void UpdateLabels()
        {
            string label = _engine.ButtonLabel;
            if (_toggle.Text != label)
            {
                _toggle.Text = label;
                _toggle.BackColor = _engine.ButtonIsHighlighted ? StartFace : BtnFace;
                _toggle.ForeColor = _engine.ButtonIsHighlighted
                    ? Color.White : Color.FromArgb(0xDE, 0xDE, 0xDE);
            }
            _status.Text = _engine.StatusText;
            _detail.Text = _engine.DetailText;
        }

        void OnDragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _engine.NoteOwnInput();   // dragging the widget is operating the widget
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
        }

        void CloseAndExit()
        {
            Log("close button pressed, exiting");
            _engine.NoteOwnInput();
            SavePosition();
            _tick.Stop();
            Application.Exit();
        }

        void RestorePosition()
        {
            // Deliberately the primary screen, not Screen.FromControl(this).
            // Before the window is positioned, FromControl resolves to whichever
            // monitor Windows happened to create it on, which put the widget on
            // the second display and made it look like it had vanished.
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int fw = P(W), fh = P(H);
            if (_cfg.SeenLeft && _cfg.SeenTop)
            {
                // Only trust a saved position that still lands on some display,
                // otherwise a resolution change would park it offscreen forever.
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(new Rectangle(_cfg.Left, _cfg.Top, fw, fh)))
                    {
                        Left = _cfg.Left;
                        Top = _cfg.Top;
                        return;
                    }
                }
            }
            // Default: top-right of the primary screen, clear of the tab bar.
            Left = wa.Right - fw - P(16);
            Top = wa.Top + P(48);
        }

        void SavePosition()
        {
            _cfg.Left = Left;
            _cfg.Top = Top;
            _cfg.Save();
        }

        /// <summary>
        /// Timestamped log of state transitions, so the machine's decisions can
        /// be inspected after the fact rather than guessed at from the UI.
        /// </summary>
        void Log(string message)
        {
            try
            {
                string path = LogPath;
                // A cycle logs a line every few seconds, so an all-day run adds
                // up. Start a fresh file rather than let it grow without bound.
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                {
                    File.Delete(path);
                    File.AppendAllText(path, "--- log truncated (1MB) " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "---" + Environment.NewLine);
                }
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    "  " + message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Losing a log line is not worth interrupting the user over, but
                // do not swallow it silently either.
                Debug.WriteLine("TabCycler: log write failed: " + ex);
            }
        }

        /// <summary>
        /// Launch the widget without taking focus. A WinForms form grabs focus
        /// by default, which here is self-defeating: the engine keys off what
        /// has foreground, so a focused widget reads as "user is elsewhere" and
        /// the whole thing sits Idle.
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _engine.WidgetHandle = Handle;

            ApplyDpi();

            // Only place the window once, after its real size is known.
            if (!_positioned)
            {
                _positioned = true;
                RestorePosition();
            }

            Log("started: interval=" + _cfg.IntervalSeconds + "s resumeDelay=" +
                _cfg.ResumeDelaySeconds + "s moveThreshold=" + _cfg.MoveThresholdPixels +
                "px dpi=" + (int)(_scale * 96) + " size=" + P(W) + "x" + P(H) +
                " at " + Left + "," + Top);
        }

        /// <summary>
        /// Re-lay out when the window lands on a monitor with a different scale.
        /// Without this the widget keeps the box size of whichever monitor it
        /// started on, which squashes the text on the other one.
        /// </summary>
        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ApplyDpi();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Edge))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    static class Native
    {
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int HTCAPTION = 0x0002;

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);
    }
}
