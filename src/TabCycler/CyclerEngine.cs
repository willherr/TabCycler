using System;
using System.Collections.Generic;

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
        int _interval;
        int _hold;
        int _moveThreshold;
        InputOptions _input;

        WatchState _state = WatchState.Holding;
        DateTime _now;
        DateTime _resumeAt;
        DateTime _nextCycleAt = DateTime.MinValue;
        CursorPos _lastMovementPos;
        bool _haveMovementPos;

        public CyclerEngine(IPlatform platform, int intervalSeconds, int holdSeconds,
                            DateTime start, int moveThresholdPixels = DefaultMoveThresholdPixels,
                            InputOptions? inputOptions = null)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            if (intervalSeconds < 1) throw new ArgumentOutOfRangeException("intervalSeconds");
            if (holdSeconds < 0) throw new ArgumentOutOfRangeException("holdSeconds");
            if (moveThresholdPixels < 1) throw new ArgumentOutOfRangeException("moveThresholdPixels");
            if (inputOptions.HasValue) inputOptions.Value.Validate();

            _p = platform;
            _interval = intervalSeconds;
            _hold = holdSeconds;
            _moveThreshold = moveThresholdPixels;
            _input = inputOptions.HasValue ? inputOptions.Value : InputOptions.Default;
            _now = start;
            _state = WatchState.Holding;
            _resumeAt = start.AddSeconds(_hold);
        }

        public int IntervalSeconds { get { return _interval; } }
        public int HoldSeconds { get { return _hold; } }
        public int MoveThresholdPixels { get { return _moveThreshold; } }
        public InputOptions Input { get { return _input; } }
        public WatchState State { get { return _state; } }

        /// <summary>
        /// The widget's own window handle. Input aimed at it is operating the
        /// widget, not the terminal, so it neither advances nor re-arms
        /// anything. Zero means "not hosted anywhere", which is what tests use.
        /// </summary>
        public IntPtr WidgetHandle { get; set; }

        /// <summary>Optional sink for state-transition messages.</summary>
        public Action<string> Log { get; set; }

        /// <summary>
        /// Applies edited settings without restarting, which is what lets the
        /// settings dialog be live. Same validation as the constructor, and the
        /// same exception on bad input, so an invalid value can never leave the
        /// engine in a state the constructor would have rejected.
        ///
        /// Changing a timing reschedules the current wait from now, so the effect
        /// is visible on the next tick rather than after whatever was already
        /// pending. Rescheduling relative to now is also the only predictable
        /// choice: preserving the old phase would make a 5 to 60 second change
        /// fire sooner than the new interval promises.
        /// </summary>
        public void ApplySettings(int intervalSeconds, int holdSeconds, int moveThresholdPixels,
                                  InputOptions inputOptions)
        {
            if (intervalSeconds < 1) throw new ArgumentOutOfRangeException("intervalSeconds");
            if (holdSeconds < 0) throw new ArgumentOutOfRangeException("holdSeconds");
            if (moveThresholdPixels < 1) throw new ArgumentOutOfRangeException("moveThresholdPixels");
            inputOptions.Validate();

            bool timingChanged = intervalSeconds != _interval || holdSeconds != _hold;
            _interval = intervalSeconds;
            _hold = holdSeconds;
            _moveThreshold = moveThresholdPixels;
            _input = inputOptions;

            if (timingChanged)
            {
                if (_state == WatchState.Cycling)
                    _nextCycleAt = _now.AddSeconds(_interval);
                else
                    _resumeAt = _now.AddSeconds(_hold);
            }

            // Logged on every apply, not only when a timing moved, so that an
            // input-option change is traceable. "Why is it not cycling" is
            // exactly the question that needs the answer in the log.
            if (Log != null)
            {
                Log("settings applied: interval=" + _interval + "s resumeDelay=" + _hold +
                    "s moveThreshold=" + _moveThreshold + "px input=[" + DescribeInput(_input) + "]" +
                    (timingChanged ? " (timing rescheduled)" : ""));
            }
        }

        /// <summary>
        /// Compact list of the input kinds that re-arm the hold. Public because
        /// the widget logs the same summary at startup, and two copies of this
        /// string would drift.
        /// </summary>
        public static string DescribeInput(InputOptions o)
        {
            string s = "";
            if (o.ResetOnKeyPress) s += "key,";
            if (o.ResetOnClick) s += "click,";
            if (o.ResetOnScroll) s += "scroll,";
            if (o.ResetOnMovement) s += "move,";
            if (o.IgnoreInjected) s += "skipInjected";
            if (s.Length == 0) return "none";
            return s.TrimEnd(',');
        }

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
                    case WatchState.Holding: return "reset by " + ArmingPhrase();
                    default: return "next tab in " + SecondsLeft(_nextCycleAt) + "s";
                }
            }
        }

        /// <summary>
        /// Plain-English list of what currently re-arms the hold, so the hint
        /// under the status cannot claim something that is switched off. With
        /// everything off there is nothing to wait for, which is worth saying
        /// plainly rather than showing an empty list.
        /// </summary>
        string ArmingPhrase()
        {
            List<string> parts = new List<string>();
            if (_input.ResetOnKeyPress) parts.Add("typing");
            if (_input.ResetOnClick) parts.Add("clicks");
            if (_input.ResetOnScroll) parts.Add("scroll");
            if (_input.ResetOnMovement) parts.Add("movement");
            if (parts.Count == 0) return "nothing, the wait always runs";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.ToArray(), 0, parts.Count - 1) + " or " + parts[parts.Count - 1];
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
        /// Throws away anything pending because the widget's own controls were
        /// clicked. Operating the widget is not terminal input, and without
        /// this the click is read as the user taking over.
        /// </summary>
        public void NoteOwnInput()
        {
            _p.DiscardPendingInput();
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

            // The hook sees its own keystrokes and flags them injected, so
            // there is no longer a stamp to reconcile afterwards.
            _p.InjectNextTab();
            _nextCycleAt = now.AddSeconds(_interval);
            Write("cycle -> sent Ctrl+Tab");
        }

        /// <summary>
        /// True when the user did something deliberate since the last poll.
        ///
        /// The platform reports typed events, so this filters on the kind and on
        /// whether it was synthesised. Movement is the one kind that still needs
        /// a distance floor: the hook delivers a WM_MOUSEMOVE per sample, so a
        /// resting hand or a jittery sensor produces a stream of one or two pixel
        /// events, and without a floor the hold re-arms on every poll and the
        /// countdown never gets past its starting value.
        /// </summary>
        bool ConsumeUserInput(DateTime now)
        {
            System.Collections.Generic.List<InputEvent> events = _p.DrainInput();
            if (events == null || events.Count == 0) return false;

            // Movement is summed across the poll rather than judged per event,
            // because a hooked mouse produces many small samples for one real
            // sweep, and a single event's size means nothing on its own.
            int movedTotal = 0;
            CursorPos previous = _lastMovementPos;
            bool sawMovement = false;

            foreach (InputEvent e in events)
            {
                // The first position seen establishes a baseline. How far the
                // pointer travelled before that is unknowable, and counting it
                // made a single small step look like a sweep across the screen.
                if (!_haveMovementPos)
                {
                    _haveMovementPos = true;
                    _lastMovementPos = e.Position;
                    previous = e.Position;
                    if (e.Kind == InputKind.Movement) continue;
                }

                if (_input.IgnoreInjected && e.Injected)
                {
                    Write("input ignored: " + e.Kind + " was injected, not the user");
                    continue;
                }
                if (!_input.ArmsFor(e.Kind))
                {
                    Write("input ignored: " + e.Kind + " is switched off in settings");
                    continue;
                }
                if (e.Kind == InputKind.Movement)
                {
                    sawMovement = true;
                    movedTotal += Math.Abs(e.Position.X - previous.X)
                                + Math.Abs(e.Position.Y - previous.Y);
                    previous = e.Position;
                    continue;
                }

                // A real key, click or scroll, and that kind is enabled.
                _lastMovementPos = e.Position;
                _resumeAt = now.AddSeconds(_hold);
                Write("input -> holding " + _hold + "s (" + e.Kind + ")");
                return true;
            }

            if (!sawMovement) return false;

            _lastMovementPos = previous;
            if (movedTotal < _moveThreshold)
            {
                Write("input ignored: moved " + movedTotal + "px total, below the " +
                      _moveThreshold + "px threshold");
                return false;
            }

            _resumeAt = now.AddSeconds(_hold);
            Write("input -> holding " + _hold + "s (moved " + movedTotal + "px)");
            return true;
        }

        void Write(string message)
        {
            Action<string> sink = Log;
            if (sink != null) sink(message);
        }
    }
}
