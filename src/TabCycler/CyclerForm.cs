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

        /// <summary>
        /// One knob for how large the widget looks, independent of the display's
        /// scale. The authored geometry is 320x82 at 96 DPI with 9pt/8.5pt type,
        /// which is too small to read comfortably on a 100% panel, where a point
        /// is only a pixel. 1.4 puts the title at 12.6pt and the body at 11.9pt,
        /// which is ordinary UI body text. Bumping this scales the box and the
        /// fonts by the same factor, so the layout stays proportional: 448x115
        /// at 100%, 672x172 at 150%.
        /// </summary>
        const float DesignScale = 1.4f;

        readonly Settings _cfg;
        readonly CyclerEngine _engine;
        readonly Timer _tick = new Timer();
        readonly Label _title = new Label();
        readonly Label _status = new Label();
        readonly Label _detail = new Label();
        readonly Button _toggle = new Button();
        readonly Button _close = new Button();

        float _scale = 1f;
        uint _lastDpi;
        bool _positioned;

        static string LogPath { get { return Path.Combine(Settings.Dir, "tabcycler.log"); } }

        public CyclerForm(IPlatform platform)
        {
            _cfg = new Settings();

            _engine = new CyclerEngine(platform, _cfg.IntervalSeconds, _cfg.ResumeDelaySeconds,
                                       DateTime.Now, _cfg.MoveThresholdPixels,
                                       _cfg.ToInputOptions())
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

        int P(int v) { return (int)Math.Round(v * _scale * DesignScale, MidpointRounding.AwayFromZero); }

        /// <summary>
        /// Lays the widget out for the DPI of the monitor it is actually on.
        ///
        /// There are two independent scalings in play and they have to agree or
        /// the widget looks wrong:
        ///
        /// 1. The box and every position are multiplied out here by _scale,
        ///    because the geometry is authored at 96 DPI.
        /// 2. The fonts are created in raw points, so WinForms realises them
        ///    against the handle's own DPI when it paints.
        ///
        /// So (1) has to be computed from the same DPI that (2) will use. That
        /// is why the dpi is a parameter instead of being read here: callers
        /// that already know the authoritative value (WM_DPICHANGED) pass it
        /// straight in, and callers that do not (first layout) read it and say
        /// so in the log, because guessing wrong is what produced the original
        /// bug. A 320x82 box holding 150% fonts jams the buttons against the
        /// edge and clips the hint text.
        /// </summary>
        void ApplyDpi(uint dpi, string source)
        {
            if (dpi == 0) dpi = 96;

            _lastDpi = dpi;
            _scale = dpi / 96f;

            ClientSize = new Size(P(W), P(H));

            _title.Font = new Font("Segoe UI", 9f * DesignScale, FontStyle.Bold);
            _title.Location = new Point(P(10), P(8));
            _title.Size = new Size(P(185), P(18));

            _status.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _status.Location = new Point(P(10), P(34));
            _status.Size = new Size(P(300), P(17));

            _detail.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _detail.Location = new Point(P(10), P(56));
            _detail.Size = new Size(P(300), P(17));

            _toggle.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _toggle.Location = new Point(P(202), P(5));
            _toggle.Size = new Size(P(72), P(26));

            _close.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _close.Location = new Point(P(280), P(5));
            _close.Size = new Size(P(30), P(26));

            Invalidate();

            Log("dpi=" + dpi + " (scale " + _scale.ToString("0.###", CultureInfo.InvariantCulture) +
                ") box=" + ClientSize.Width + "x" + ClientSize.Height + " via " + source +
                " at " + Left + "," + Top);
        }

        /// <summary>
        /// First layout, before the window is known to sit on a particular
        /// monitor. GetDpiForWindow can answer 96 here even when the widget is
        /// about to land on a 150% display, which is why OnShown re-checks.
        /// </summary>
        void ApplyDpi()
        {
            ApplyDpi(Native.GetDpiForWindow(Handle), "GetDpiForWindow");
        }


        /// <summary>
        /// Compact description of which inputs count, for the startup log. The
        /// log is the only way to tell what the widget is actually doing when
        /// the UI is too small to read.
        /// </summary>
        string DescribeInput()
        {
            StringBuilder sb = new StringBuilder();
            if (_cfg.ResetOnKeyPress) sb.Append("key,");
            if (_cfg.ResetOnClick) sb.Append("click,");
            if (_cfg.ResetOnScroll) sb.Append("scroll,");
            if (_cfg.ResetOnMovement) sb.Append("move,");
            if (_cfg.IgnoreInjected) sb.Append("skipInjected");
            string s = sb.ToString();
            return s.Length == 0 ? "none" : s.TrimEnd(',');
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

            // dpi, box size and position are logged by ApplyDpi, which owns
            // them. They are deliberately not repeated here: at this point the
            // window has only just been placed, so the DPI here can still be
            // the pre-move guess that OnShown corrects.
            Log("started: interval=" + _cfg.IntervalSeconds + "s resumeDelay=" +
                _cfg.ResumeDelaySeconds + "s moveThreshold=" + _cfg.MoveThresholdPixels +
                "px input=[" + DescribeInput() + "]");
        }

        /// <summary>
        /// A top-level form moving between monitors is told about the new scale
        /// through WM_DPICHANGED, not through OnDpiChangedAfterParent (that one
        /// is for a child whose parent changed, so it never fired here and
        /// dragging the widget between the 150% display and the 100% panel
        /// resized nothing).
        ///
        /// LOWORD(wParam) is the new DPI and is authoritative. The base call
        /// runs first so WinForms has updated its own bookkeeping before the
        /// layout is recomputed from the same value. The suggested rectangle in
        /// lParam is deliberately not applied: the widget owns its own
        /// position, and honouring it would fight the drag handler.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED)
            {
                uint dpi = (uint)(m.WParam.ToInt64() & 0xFFFF);
                base.WndProc(ref m);
                ApplyDpi(dpi, "WM_DPICHANGED");
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// Re-read the DPI once the window is actually on screen. The first
        /// layout happens before RestorePosition has moved the widget, so it
        /// can be sized for a monitor the widget is not on. This is the check
        /// that makes the initial position come out right in both directions.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyDpi(Native.GetDpiForWindow(Handle), "shown");
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
        internal const int WM_DPICHANGED = 0x02E0;

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);
    }
}
