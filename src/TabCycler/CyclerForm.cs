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
    internal sealed class CyclerForm : DpiAwareForm
    {
        // Colours live in Palette, so the settings dialog matches the widget
        // without a second copy of the hex values to drift out of step.

        // Geometry is authored at 96 DPI and multiplied out at runtime. See
        // OnDpiScaled for why that is not simply done once in the constructor.
        const int W = 384, H = 82;

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
        readonly Button _gear = new Button();
        readonly Button _close = new Button();

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
            BackColor = Palette.Back;
            ForeColor = Palette.Body;
            TopMost = true;                 // stay above the terminal and everything else
            ShowInTaskbar = true;           // own taskbar button, pinnable
            MinimizeBox = false;
            MaximizeBox = false;
            // Deliberately None. WinForms' own DPI autoscaling reads the DPI via
            // CreateGraphics(), which reports 96 on a 150% display, so it
            // silently declines to scale. DpiAwareForm does it properly instead.
            AutoScaleMode = AutoScaleMode.None;

            _title.Text = "Tab Cycler";
            _title.ForeColor = Palette.Title;
            _title.BackColor = Color.Transparent;
            _title.TextAlign = ContentAlignment.MiddleLeft;

            _status.ForeColor = Palette.Body;
            _status.BackColor = Color.Transparent;
            _status.TextAlign = ContentAlignment.MiddleLeft;

            _detail.ForeColor = Palette.Hint;
            _detail.BackColor = Color.Transparent;
            _detail.TextAlign = ContentAlignment.MiddleLeft;

            ConfigureButton(_toggle);
            ConfigureButton(_gear);
            ConfigureButton(_close);
            // The gear is drawn rather than typed. A literal U+2699 in the
            // source is a gamble: csc reads this file as UTF-8 only when it has
            // a BOM, and without one the glyph can arrive mangled, which is
            // exactly the kind of thing that only shows up on someone else's
            // build. Painting it also keeps it crisp at 100% and 150%.
            _gear.Text = "";
            _gear.Paint += delegate(object sender, PaintEventArgs e)
            {
                OnGearPaint(e.Graphics);
            };
            _close.Text = "X";
            _close.ForeColor = Palette.CloseLabel;
            _close.MouseEnter += delegate { _close.BackColor = Palette.Danger; _close.ForeColor = Color.White; };
            _close.MouseLeave += delegate { _close.BackColor = Palette.BtnFace; _close.ForeColor = Palette.CloseLabel; };
            _toggle.MouseEnter += delegate { if (_toggle.BackColor == Palette.BtnFace) _toggle.BackColor = Palette.BtnHover; };
            _toggle.MouseLeave += delegate { if (_toggle.BackColor == Palette.BtnFace) _toggle.BackColor = Palette.BtnFace; };
            _gear.MouseEnter += delegate { if (_gear.BackColor == Palette.BtnFace) _gear.BackColor = Palette.BtnHover; };
            _gear.MouseLeave += delegate { if (_gear.BackColor == Palette.BtnFace) _gear.BackColor = Palette.BtnFace; };

            _toggle.Click += delegate { OnToggleClick(); };
            _gear.Click += delegate { OnSettingsClick(); };
            _close.Click += delegate { CloseAndExit(); };

            Controls.AddRange(new Control[] { _title, _status, _detail, _toggle, _gear, _close });
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
            Palette.StyleButton(b);
        }

        int P(int v) { return P96((int)Math.Round(v * DesignScale, MidpointRounding.AwayFromZero)); }

        /// <summary>
        /// Draws the settings gear: a ring, a hub, and teeth around it. Sized
        /// from the button so it lands the same way on both displays.
        /// </summary>
        void OnGearPaint(Graphics g)
        {
            using (Pen p = new Pen(Palette.BtnLabel))
            using (SolidBrush b = new SolidBrush(Palette.BtnLabel))
            {
                float d = Math.Min(_gear.Width, _gear.Height) * 0.52f;
                float cx = _gear.Width / 2f, cy = _gear.Height / 2f;
                float r = d / 2f;

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawEllipse(p, cx - r, cy - r, d, d);

                float hub = d * 0.34f;
                g.FillEllipse(b, cx - hub / 2f, cy - hub / 2f, hub, hub);

                // Six teeth, as short thick spokes just outside the ring.
                const int Teeth = 6;
                for (int i = 0; i < Teeth; i++)
                {
                    double a = (Math.PI * 2 * i / Teeth) - Math.PI / 2;
                    float inner = r * 0.82f;
                    float outer = r * 1.34f;
                    float x1 = cx + (float)Math.Cos(a) * inner;
                    float y1 = cy + (float)Math.Sin(a) * inner;
                    float x2 = cx + (float)Math.Cos(a) * outer;
                    float y2 = cy + (float)Math.Sin(a) * outer;
                    float tw = d * 0.15f;
                    g.DrawLine(p, x1, y1, x2, y2);
                    using (SolidBrush t = new SolidBrush(Palette.BtnLabel))
                        g.FillEllipse(t, x2 - tw / 2f, y2 - tw / 2f, tw, tw);
                }
            }
        }

        /// <summary>
        /// Lays the widget out for the DPI of the monitor it is actually on.
        ///
        /// There are two independent scalings in play and they have to agree or
        /// the widget looks wrong:
        ///
        /// 1. The box and every position are multiplied out here by the scale,
        ///    because the geometry is authored at 96 DPI.
        /// 2. The fonts are created in raw points, so WinForms realises them
        ///    against the handle's own DPI when it paints.
        ///
        /// So (1) has to be computed from the same DPI that (2) will use, which
        /// is why the base class passes the dpi in rather than letting this
        /// method read it again. A 320x82 box holding 150% fonts jams the
        /// buttons against the edge and clips the hint text.
        ///
        /// The font point sizes are multiplied by <see cref="DesignScale"/>
        /// because that knob is a deliberate legibility choice, not a DPI
        /// correction, and the geometry goes through P() which applies it too.
        /// </summary>
        protected override void OnDpiScaled(uint dpi, float scale, string source)
        {
            ClientSize = new Size(P(W), P(H));

            _title.Font = new Font("Segoe UI", 9f * DesignScale, FontStyle.Bold);
            _title.Location = new Point(P(10), P(8));
            _title.Size = new Size(P(228), P(18));

            _status.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _status.Location = new Point(P(10), P(34));
            _status.Size = new Size(P(340), P(17));

            _detail.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _detail.Location = new Point(P(10), P(56));
            _detail.Size = new Size(P(340), P(17));

            _toggle.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _toggle.Location = new Point(P(245), P(5));
            _toggle.Size = new Size(P(66), P(26));

            _gear.Font = new Font("Segoe UI", 11f * DesignScale);
            _gear.Location = new Point(P(315), P(5));
            _gear.Size = new Size(P(30), P(26));

            _close.Font = new Font("Segoe UI", 8.5f * DesignScale);
            _close.Location = new Point(P(349), P(5));
            _close.Size = new Size(P(30), P(26));

            Invalidate();
        }

        /// <summary>Override point for the base class's DPI log line.</summary>
        protected override void LogDpi(string message)
        {
            Log(message + " box=" + ClientSize.Width + "x" + ClientSize.Height +
                " at " + Left + "," + Top);
        }

        /// <summary>
        /// The input summary for the startup log, delegated to the engine so
        /// there is one definition of the string.
        /// </summary>
        string DescribeInput() { return CyclerEngine.DescribeInput(_engine.Input); }

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
                _toggle.BackColor = _engine.ButtonIsHighlighted ? Palette.StartFace : Palette.BtnFace;
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
            // The base class lays the window out for the current DPI before
            // this runs, so the size is already right for RestorePosition to
            // clamp against.
            base.OnHandleCreated(e);
            _engine.WidgetHandle = Handle;

            // Only place the window once, after its real size is known.
            if (!_positioned)
            {
                _positioned = true;
                RestorePosition();
            }

            // dpi, box size and position are logged by the base class, which
            // owns them. They are deliberately not repeated here: at this point
            // the window has only just been placed, so the DPI can still be the
            // pre-move guess that OnShown corrects.
            Log("started: interval=" + _cfg.IntervalSeconds + "s resumeDelay=" +
                _cfg.ResumeDelaySeconds + "s moveThreshold=" + _cfg.MoveThresholdPixels +
                "px input=[" + DescribeInput() + "]");
        }

        /// <summary>
        /// Opens the settings dialog and applies whatever comes back. The dialog
        /// edits a copy, so cancelling writes nothing and changes nothing.
        ///
        /// The engine takes the new values live and the tick period is
        /// rescheduled to match, which is what makes the change visible without
        /// a restart. Applying to the engine before saving means a rejected value
        /// cannot leave the file and the running state disagreeing.
        /// </summary>
        void OnSettingsClick()
        {
            _engine.NoteOwnInput();

            using (SettingsDialog dlg = new SettingsDialog(_engine))
            {
                // Place it against the widget rather than letting Windows decide,
                // because a default-placed dialog likes to land on the other
                // monitor when the widget is on the second display. The widget
                // sits near the top right by default, so centring on it puts the
                // dialog half off the right edge, hence the clamp to whichever
                // monitor the widget is on.
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                dlg.StartPosition = FormStartPosition.Manual;
                dlg.Location = new Point(
                    Math.Max(wa.Left, Math.Min(wa.Right - dlg.Width,
                        Left + (Width - dlg.Width) / 2)),
                    Math.Max(wa.Top, Math.Min(wa.Bottom - dlg.Height,
                        Top + (Height - dlg.Height) / 2)));
                dlg.ShowDialog(this);

                if (!dlg.Accepted) return;

                InputOptions opts = new InputOptions();
                opts.ResetOnKeyPress = dlg.ResetOnKeyPress;
                opts.ResetOnClick = dlg.ResetOnClick;
                opts.ResetOnScroll = dlg.ResetOnScroll;
                opts.ResetOnMovement = dlg.ResetOnMovement;
                opts.IgnoreInjected = dlg.IgnoreInjected;

                try
                {
                    _engine.ApplySettings(dlg.IntervalSeconds, dlg.ResumeDelaySeconds,
                                          dlg.MoveThresholdPixels, opts);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    // Should be unreachable, because the dialog's spin controls
                    // enforce the same ranges. Logged rather than swallowed, and
                    // the file is left alone so the two cannot disagree.
                    Log("settings rejected, not saved: " + ex.Message);
                    return;
                }

                _tick.Interval = _engine.IntervalSeconds * 1000;

                _cfg.IntervalSeconds = _engine.IntervalSeconds;
                _cfg.ResumeDelaySeconds = _engine.HoldSeconds;
                _cfg.MoveThresholdPixels = _engine.MoveThresholdPixels;
                _cfg.ResetOnKeyPress = opts.ResetOnKeyPress;
                _cfg.ResetOnClick = opts.ResetOnClick;
                _cfg.ResetOnScroll = opts.ResetOnScroll;
                _cfg.ResetOnMovement = opts.ResetOnMovement;
                _cfg.IgnoreInjected = opts.IgnoreInjected;
                _cfg.Save();

                UpdateLabels();
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Palette.Edge))
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
