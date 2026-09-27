using System;
using System.Drawing;
using System.Windows.Forms;

namespace TabCycler
{
    /// <summary>
    /// The settings dialog for #9. Modal, because there is one owner and a
    /// single OK, and because a modeless window would need its own focus and
    /// lifetime handling for no benefit here.
    ///
    /// It edits a copy and hands the result back through its properties, so
    /// Cancel is genuinely free: nothing is written and nothing is applied until
    /// the caller decides to.
    ///
    /// The values are seeded from the engine rather than from the settings file,
    /// so the dialog can never open showing something other than what is
    /// actually running.
    ///
    /// Geometry is authored at 96 DPI and scaled through
    /// <see cref="DpiAwareForm"/>, the same path the widget uses, because this
    /// window is just as likely to open on the 150% external display.
    /// </summary>
    internal sealed class SettingsDialog : DpiAwareForm
    {
        // Authored at 96 DPI, like the widget, and scaled by the same helper
        // so the dialog and the widget match on a 150% display.
        const int IntervalMin = 1, IntervalMax = 600;
        const int DelayMin = 0, DelayMax = 3600;
        const int ThresholdMin = 1, ThresholdMax = 200;

        readonly NumericUpDown _interval = new NumericUpDown();
        readonly NumericUpDown _delay = new NumericUpDown();
        readonly NumericUpDown _threshold = new NumericUpDown();
        readonly CheckBox _type = new CheckBox();
        readonly CheckBox _click = new CheckBox();
        readonly CheckBox _scroll = new CheckBox();
        readonly CheckBox _move = new CheckBox();
        readonly CheckBox _injected = new CheckBox();

        readonly Button _ok = new Button();
        readonly Button _cancel = new Button();
        readonly Button _defaults = new Button();
        readonly Label _headingTiming = new Label();
        readonly Label _headingResume = new Label();
        readonly Label _lblInterval = new Label();
        readonly Label _lblDelay = new Label();
        readonly Label _lblThreshold = new Label();
        readonly Label _unitSeconds = new Label();
        readonly Label _unitSeconds2 = new Label();
        readonly Label _unitPixels = new Label();

        const int W = 380, H = 360;

        /// <summary>True when the user pressed Save rather than Cancel.</summary>
        public bool Accepted { get; private set; }

        public int IntervalSeconds { get { return (int)_interval.Value; } }
        public int ResumeDelaySeconds { get { return (int)_delay.Value; } }
        public int MoveThresholdPixels { get { return (int)_threshold.Value; } }

        public bool ResetOnKeyPress { get { return _type.Checked; } }
        public bool ResetOnClick { get { return _click.Checked; } }
        public bool ResetOnScroll { get { return _scroll.Checked; } }
        public bool ResetOnMovement { get { return _move.Checked; } }
        public bool IgnoreInjected { get { return _injected.Checked; } }

        public SettingsDialog(CyclerEngine engine)
        {
            if (engine == null) throw new ArgumentNullException("engine");

            Text = "Tab Cycler settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Palette.Back;
            ForeColor = Palette.Body;

            MakeHeading(_headingTiming, "Timing");
            MakeHeading(_headingResume, "Resume when you");
            MakeFieldLabel(_lblInterval, "Cycle every");
            MakeFieldLabel(_lblDelay, "Wait after you stop");
            MakeFieldLabel(_lblThreshold, "Mouse movement that counts");
            MakeUnit(_unitSeconds, "seconds");
            MakeUnit(_unitSeconds2, "seconds");
            MakeUnit(_unitPixels, "pixels");

            MakeNumber(_interval, IntervalMin, IntervalMax);
            MakeNumber(_delay, DelayMin, DelayMax);
            MakeNumber(_threshold, ThresholdMin, ThresholdMax);

            _type.Text = "type";
            _click.Text = "click";
            _scroll.Text = "scroll the terminal";
            _move.Text = "move the mouse";
            _injected.Text = "ignore input sent by other programs";
            foreach (CheckBox box in new CheckBox[] { _type, _click, _scroll, _move, _injected })
                MakeCheck(box);

            MakeDialogButton(_ok, "Save", 84);
            MakeDialogButton(_cancel, "Cancel", 84);
            MakeDialogButton(_defaults, "Defaults", 90);

            _ok.Click += delegate { Accepted = true; DialogResult = DialogResult.OK; Close(); };
            _cancel.Click += delegate { Accepted = false; DialogResult = DialogResult.Cancel; Close(); };
            _defaults.Click += delegate { ApplyDefaults(); };

            AcceptButton = _ok;
            CancelButton = _cancel;

            Controls.AddRange(new Control[]
            {
                _headingTiming, _headingResume,
                _lblInterval, _lblDelay, _lblThreshold,
                _unitSeconds, _unitSeconds2, _unitPixels,
                _interval, _delay, _threshold,
                _type, _click, _scroll, _move, _injected,
                _ok, _cancel, _defaults
            });

            Seed(engine);
        }

        /// <summary>
        /// Geometry, authored at 96 DPI and scaled like everything else. Only
        /// the positions and the box go through P(): the fonts stay in raw
        /// points, because WinForms realises those against the handle's own DPI,
        /// which is the same split the widget relies on.
        /// </summary>
        protected override void OnDpiScaled(uint dpi, float scale, string source)
        {
            ClientSize = new Size(P96(W), P96(H));
            Font = new Font("Segoe UI", 9f);

            _headingTiming.Location = new Point(P96(14), P96(6));
            // 124, not closer: at 118 this sat almost on top of the unit label
            // of the row above and read as part of it.
            _headingResume.Location = new Point(P96(14), P96(124));

            _lblInterval.Location = new Point(P96(14), P96(33));
            _lblDelay.Location = new Point(P96(14), P96(63));
            _lblThreshold.Location = new Point(P96(14), P96(93));

            _interval.Location = new Point(P96(200), P96(28));
            _delay.Location = new Point(P96(200), P96(58));
            _threshold.Location = new Point(P96(200), P96(88));
            _unitSeconds.Location = new Point(P96(288), P96(33));
            _unitSeconds2.Location = new Point(P96(288), P96(63));
            _unitPixels.Location = new Point(P96(288), P96(93));

            int y = 150;
            foreach (CheckBox box in new CheckBox[] { _type, _click, _scroll, _move, _injected })
            {
                box.Location = new Point(P96(16), P96(y));
                y += 25;
            }

            int by = H - 46;
            _defaults.Location = new Point(P96(16), P96(by));
            _cancel.Location = new Point(P96(W - 188), P96(by));
            _ok.Location = new Point(P96(W - 100), P96(by));
        }

        static void MakeHeading(Label l, string text)
        {
            l.Text = text;
            l.ForeColor = Palette.Title;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.Size = new Size(320, 18);
        }

        static void MakeFieldLabel(Label l, string text)
        {
            l.Text = text;
            l.ForeColor = Palette.Body;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.Size = new Size(180, 20);
            l.TextAlign = ContentAlignment.MiddleLeft;
        }

        static void MakeUnit(Label l, string text)
        {
            l.Text = text;
            l.ForeColor = Palette.Hint;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.Size = new Size(80, 20);
            l.TextAlign = ContentAlignment.MiddleLeft;
        }

        static void MakeNumber(NumericUpDown box, int min, int max)
        {
            box.Minimum = min;
            box.Maximum = max;
            box.Value = min;
            box.Width = 78;
            box.BackColor = Palette.BtnFace;
            box.ForeColor = Palette.Title;
            box.TextAlign = HorizontalAlignment.Right;
            box.BorderStyle = BorderStyle.FixedSingle;
        }

        static void MakeCheck(CheckBox box)
        {
            box.ForeColor = Palette.Body;
            box.BackColor = Color.Transparent;
            box.FlatStyle = FlatStyle.Flat;
            box.FlatAppearance.BorderSize = 0;
            box.AutoSize = true;
        }

        static void MakeDialogButton(Button b, string text, int width)
        {
            b.Text = text;
            b.Size = new Size(width, 28);
            Palette.StyleButton(b);
        }

        /// <summary>Seeds the controls from what is actually running.</summary>
        void Seed(CyclerEngine engine)
        {
            _interval.Value = Clamp(_interval, engine.IntervalSeconds);
            _delay.Value = Clamp(_delay, engine.HoldSeconds);
            _threshold.Value = Clamp(_threshold, engine.MoveThresholdPixels);

            InputOptions o = engine.Input;
            _type.Checked = o.ResetOnKeyPress;
            _click.Checked = o.ResetOnClick;
            _scroll.Checked = o.ResetOnScroll;
            _move.Checked = o.ResetOnMovement;
            _injected.Checked = o.IgnoreInjected;
        }

        void ApplyDefaults()
        {
            _interval.Value = 5;
            _delay.Value = 60;
            _threshold.Value = CyclerEngine.DefaultMoveThresholdPixels;
            InputOptions d = InputOptions.Default;
            _type.Checked = d.ResetOnKeyPress;
            _click.Checked = d.ResetOnClick;
            _scroll.Checked = d.ResetOnScroll;
            _move.Checked = d.ResetOnMovement;
            _injected.Checked = d.IgnoreInjected;
        }

        /// <summary>
        /// A settings file edited by hand can hold a value outside the spin
        /// control's range, and assigning that to NumericUpDown.Value throws.
        /// Clamping here means an odd file opens the dialog instead of crashing
        /// the widget on the way past it.
        /// </summary>
        static decimal Clamp(NumericUpDown box, int value)
        {
            if (value < (int)box.Minimum) return box.Minimum;
            if (value > (int)box.Maximum) return box.Maximum;
            return value;
        }
    }
}
