using System;

namespace TabCycler
{
    /// <summary>
    /// The whole tab-cycling state machine. No WinForms, no P/Invoke, and no
    /// clock of its own: the caller passes the current time in, and the machine
    /// only reports what should happen rather than reaching out to do it. That
    /// is what turns "does pause actually stay paused" from something you have
    /// to poke a live window to answer into an assertion in a test.
    /// </summary>
    public sealed class CyclerEngine
    {
        /// <summary>
        /// How far the pointer has to travel in one poll before the movement
        /// counts as deliberate. Below this it is treated as jitter.
        /// </summary>
        public const int DefaultMoveThresholdPixels = 8;

        readonly IPlatform _p;
        readonly int _interval;
        readonly int _hold;
        readonly int _moveThreshold;

        WatchState _state = WatchState.Holding;
        DateTime _now;
        DateTime _resumeAt;
        DateTime _nextCycleAt = DateTime.MinValue;

        uint _lastStamp;
        bool _haveStamp;
        CursorPos _lastCursor;

        public CyclerEngine(IPlatform platform, int intervalSeconds, int holdSeconds,
                            DateTime start, int moveThresholdPixels = DefaultMoveThresholdPixels)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            if (intervalSeconds < 1) throw new ArgumentOutOfRangeException("intervalSeconds");
            if (holdSeconds < 0) throw new ArgumentOutOfRangeException("holdSeconds");
            if (moveThresholdPixels < 1) throw new ArgumentOutOfRangeException("moveThresholdPixels");

            _p = platform;
            _interval = intervalSeconds;
            _hold = holdSeconds;
            _moveThreshold = moveThresholdPixels;
            _now = start;
            _state = WatchState.Holding;
            _resumeAt = start.AddSeconds(_hold);

            // Treat whatever the user did before launch as already consumed, so
            // starting the widget does not immediately report fresh input.
            CaptureBaseline();
        }

        public int IntervalSeconds { get { return _interval; } }
        public int HoldSeconds { get { return _hold; } }
        public int MoveThresholdPixels { get { return _moveThreshold; } }
        public WatchState State { get { return _state; } }

        /// <summary>
        /// The widget's own window handle. Input aimed at it is operating the
        /// widget, not the terminal, so it neither advances nor re-arms
        /// anything. Zero means "not hosted anywhere", which is what tests use.
        /// </summary>
        public IntPtr WidgetHandle { get; set; }

        /// <summary>Optional sink for state-transition messages.</summary>
        public Action<string> Log { get; set; }

        // ---- what the widget shows -------------------------------------------

        /// <summary>
        /// The button always offers the one action that makes sense next, and is
        /// derived from the same state Toggle reads, so the label can never
        /// promise something the click does not do.
        /// </summary>
        public string ButtonLabel { get { return _state == WatchState.Cycling ? "Pause" : "Start"; } }

        public bool ButtonIsHighlighted { get { return _state != WatchState.Cycling; } }

        public string StatusText
        {
            get
            {
                switch (_state)
                {
                    case WatchState.Away: return "Idle";
                    case WatchState.Holding: return "Holding for " + SecondsLeft(_resumeAt) + "s";
                    default: return "Cycling every " + _interval + "s";
                }
            }
        }

        public string DetailText
        {
            get
            {
                switch (_state)
                {
                    case WatchState.Away: return "focus Windows Terminal to start";
                    case WatchState.Holding: return "reset by typing, clicks, scroll or movement";
                    default: return "next tab in " + SecondsLeft(_nextCycleAt) + "s";
                }
            }
        }

        int SecondsLeft(DateTime when)
        {
            int s = (int)Math.Ceiling((when - _now).TotalSeconds);
            return s < 0 ? 0 : s;
        }

        // ---- driving it ------------------------------------------------------

        /// <summary>Advance the machine by one poll. Pass the current time.</summary>
        public void Tick(DateTime now)
        {
            _now = now;
            IntPtr fg = _p.GetForegroundWindow();

            // Operating the widget: hold everything exactly as it is.
            if (fg == WidgetHandle && fg != IntPtr.Zero) return;

            if (!_p.IsTerminalWindow(fg))
            {
                if (_state != WatchState.Away)
                {
                    _state = WatchState.Away;
                    _nextCycleAt = DateTime.MinValue;
                    Write("left Windows Terminal -> Idle");
                }
                return;
            }

            switch (_state)
            {
                case WatchState.Away:
                    // Focus arrived at the terminal. Always start a full fresh
                    // hold: reusing whatever deadline was left over meant that
                    // alt-tabbing away and straight back only waited out the
                    // remainder, instead of the full minute.
                    ConsumeUserInput(now);
                    _resumeAt = now.AddSeconds(_hold);
                    _state = WatchState.Holding;
                    Write("terminal focused -> holding " + _hold + "s");
                    break;

                case WatchState.Holding:
                    if (ConsumeUserInput(now))
                    {
                        _resumeAt = now.AddSeconds(_hold);
                    }
                    else if (now >= _resumeAt)
                    {
                        _state = WatchState.Cycling;
                        // One full interval before the first switch, so whatever
                        // the user just looked at stays readable.
                        _nextCycleAt = now.AddSeconds(_interval);
                        Write("hold elapsed -> cycling every " + _interval + "s");
                    }
                    break;

                case WatchState.Cycling:
                    if (ConsumeUserInput(now))
                    {
                        _state = WatchState.Holding;
                        _resumeAt = now.AddSeconds(_hold);
                    }
                    else if (now >= _nextCycleAt)
                    {
                        Cycle(now);
                    }
                    break;
            }
        }

        /// <summary>The Start/Pause button was pressed.</summary>
        public void Toggle(DateTime now)
        {
            _now = now;
            if (_state == WatchState.Cycling)
            {
                // Pausing is not its own state: it is the same hold that follows
                // a return or a keystroke, so there is one way to be stopped
                // and one way out of it.
                _state = WatchState.Holding;
                _resumeAt = now.AddSeconds(_hold);
                Write("pause pressed -> holding for " + _hold + "s");
            }
            else
            {
                _state = WatchState.Cycling;
                _resumeAt = DateTime.MinValue;
                _nextCycleAt = now.AddSeconds(_interval);
                Write("start pressed -> first tab in " + _interval + "s, then every " + _interval + "s");
            }
        }

        /// <summary>
        /// Marks the current input stamp as handled because it was the widget's
        /// own button. Without this the click is read as the user taking over
        /// and the hold springs back on the very next poll.
        /// </summary>
        public void NoteOwnInput()
        {
            _lastStamp = _p.LastInputStamp();
            _haveStamp = true;
        }

        void Cycle(DateTime now)
        {
            // Re-check focus immediately before injecting: the keystroke goes to
            // whatever is foreground at that instant, so narrow the gap.
            if (!_p.IsTerminalWindow(_p.GetForegroundWindow()))
            {
                Write("focus changed mid-cycle, backing off -> Idle");
                _state = WatchState.Away;
                return;
            }

            _p.InjectNextTab();
            // The injection bumps the system input stamp; record it so it is
            // not read back as the user on the next poll.
            NoteOwnInput();
            _nextCycleAt = now.AddSeconds(_interval);
            Write("cycle -> sent Ctrl+Tab");
        }

        /// <summary>
        /// True when the user did something deliberate since the last poll:
        /// a key press, a click, a wheel event, or a real movement of the
        /// pointer.
        ///
        /// Wheel events and clicks leave the pointer exactly where it was, so
        /// "the stamp changed but the cursor did not move" catches those. A
        /// pointer that did move is also deliberate, but only once it has
        /// travelled further than the threshold in a single poll. Without that
        /// floor, sensor jitter and a resting hand re-armed the hold on every
        /// poll and the countdown never got past its starting value, which is
        /// what made the widget look stuck at 60.
        /// </summary>
        bool ConsumeUserInput(DateTime now)
        {
            uint stamp = _p.LastInputStamp();
            bool isNew = !_haveStamp || stamp != _lastStamp;

            CursorPos cursor = _p.CursorPosition();
            int dx = Math.Abs(cursor.X - _lastCursor.X);
            int dy = Math.Abs(cursor.Y - _lastCursor.Y);
            _lastCursor = cursor;

            if (isNew)
            {
                _lastStamp = stamp;
                _haveStamp = true;
            }

            if (!isNew) return false;

            bool pointerHeldStill = (dx == 0 && dy == 0);
            bool movedDeliberately = (dx >= _moveThreshold || dy >= _moveThreshold);
            if (!pointerHeldStill && !movedDeliberately) return false;

            _resumeAt = now.AddSeconds(_hold);
            Write("input -> holding " + _hold + "s");
            return true;
        }

        void CaptureBaseline()
        {
            _lastStamp = _p.LastInputStamp();
            _haveStamp = true;
            _lastCursor = _p.CursorPosition();
        }

        void Write(string message)
        {
            Action<string> sink = Log;
            if (sink != null) sink(message);
        }
    }
}
