using System;

namespace TabCycler.Tests
{
    /// <summary>
    /// Behavioural tests for the cycler. Every test here corresponds to
    /// something that actually went wrong during development, or to the
    /// requirement that produced the tool in the first place.
    ///
    /// The times are synthetic and passed in explicitly, so there is no
    /// sleeping and no dependence on a real clock.
    /// </summary>
    public sealed class EngineTests
    {
        const int Interval = 5;
        const int Hold = 60;

        static readonly DateTime T0 = new DateTime(2026, 1, 1, 9, 0, 0);

        FakePlatform _p;
        LogSink _log;
        CyclerEngine _e;

        void Setup()
        {
            _p = new FakePlatform();
            _log = new LogSink();
            _e = new CyclerEngine(_p, Interval, Hold, T0);
            _e.Log = _log.Add;
        }

        /// <summary>Poll the engine repeatedly, as the 250ms timer would.</summary>
        void Poll(DateTime start, int seconds, int stepMs = 250)
        {
            DateTime t = start;
            DateTime end = start.AddSeconds(seconds);
            while (t < end)
            {
                _e.Tick(t);
                t = t.AddMilliseconds(stepMs);
            }
        }

        /// <summary>
        /// Tick at exact whole-second offsets from a start time. Used where a
        /// test cares about an exact boundary, so that an off-by-one in a loop
        /// range cannot be mistaken for an engine bug.
        /// </summary>
        void TickAt(DateTime start, params int[] offsetsSeconds)
        {
            foreach (int s in offsetsSeconds) _e.Tick(start.AddSeconds(s));
        }

        /// <summary>Get the engine into Cycling, having skipped the initial hold.</summary>
        DateTime ReachCycling()
        {
            _e.Toggle(T0);                       // Start
            return T0.AddSeconds(Interval);
        }

        // ---- construction ---------------------------------------------------

        [Test("rejects a null platform")]
        public void RejectsNullPlatform()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CyclerEngine(null, Interval, Hold, T0),
                "a null platform must be rejected rather than fail later");
        }

        [Test("rejects a zero or negative interval")]
        public void RejectsBadInterval()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CyclerEngine(new FakePlatform(), 0, Hold, T0),
                "an interval below 1s would spin");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CyclerEngine(new FakePlatform(), -1, Hold, T0),
                "a negative interval is meaningless");
        }

        [Test("rejects a negative hold delay")]
        public void RejectsNegativeHold()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CyclerEngine(new FakePlatform(), Interval, -1, T0),
                "a negative hold is meaningless");
        }

        [Test("starts in Holding with the full delay pending")]
        public void StartsHolding()
        {
            Setup();
            Assert.Same(WatchState.Holding, _e.State, "launching should not immediately cycle");
            Assert.Equal("Start", _e.ButtonLabel, "the button offers to start");
            Assert.Equal("Holding for 60s", _e.StatusText, "shows the pending delay");
        }

        [Test("input from before launch is not treated as fresh")]
        public void PreLaunchInputNotCounted()
        {
            Setup();
            // A stamp that changed since construction, as it always has.
            _p.TypeKey();
            _e.Tick(T0);
            _e.Tick(T0.AddSeconds(2));

            // A reset would put this back to a full 60. It must have moved on.
            Assert.False(_e.StatusText == "Holding for 60s",
                "pre-launch input must not re-arm the hold, it should just count down");
        }

        // ---- the hold -------------------------------------------------------

        [Test("Holding expires into Cycling once the delay passes")]
        public void HoldingExpires()
        {
            Setup();
            Poll(T0, Hold + 2);
            Assert.Same(WatchState.Cycling, _e.State, "60s of quiet should start cycling");
            Assert.Equal("Pause", _e.ButtonLabel, "now it offers to pause");
            Assert.True(_log.Saw("hold elapsed"), "the transition is logged");
        }

        [Test("Holding does not expire early")]
        public void HoldingDoesNotExpireEarly()
        {
            Setup();
            Poll(T0, Hold - 2);
            Assert.Same(WatchState.Holding, _e.State, "must still be holding one second before");
        }

        [Test("Cycling waits a full interval before the first switch")]
        public void FirstSwitchWaitsFullInterval()
        {
            Setup();
            // The hold expires at t=60, and that is when Cycling begins.
            TickAt(T0, 59, 60);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling from t=60");
            Assert.Equal(0, _p.Injections, "entering Cycling must not switch immediately");

            DateTime from = T0.AddSeconds(60);
            TickAt(from, 1, 2, 3, 4);
            Assert.Equal(0, _p.Injections, "one second short of the interval, still nothing");

            _e.Tick(from.AddSeconds(5));
            Assert.Equal(1, _p.Injections, "and exactly one switch at the interval");
        }

        [Test("Cycling keeps to the interval")]
        public void CyclingRespectsInterval()
        {
            Setup();
            TickAt(T0, 60);                                   // cycling begins
            DateTime from = T0.AddSeconds(60);
            TickAt(from, 5, 10, 15, 20, 25);                 // five intervals
            Assert.Equal(5, _p.Injections, "one switch per interval, no more");
        }

        // ---- user input -----------------------------------------------------

        [Test("typing during Cycling stops it and starts a fresh hold")]
        public void TypingStopsCycling()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Equal(1, _p.Injections, "precondition: it was cycling");

            _p.TypeKey();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State, "a keystroke should stop the cycling");
            Assert.Equal("Holding for 60s", _e.StatusText, "and restart the full delay");
        }

        [Test("clicking during Cycling stops it too")]
        public void ClickingStopsCycling()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _p.Click();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State, "a click is deliberate input");
        }

        [Test("repeated input keeps pushing the hold out, so it never resumes")]
        public void RepeatedInputKeepsExtendingHold()
        {
            Setup();
            DateTime c = ReachCycling();
            DateTime t = c;
            // Type every 5s for five minutes. With a 60s hold this must never cycle.
            for (int i = 0; i < 60; i++)
            {
                _p.TypeKey();
                _e.Tick(t);
                t = t.AddSeconds(5);
                Assert.Same(WatchState.Holding, _e.State,
                    "continuous input should keep it stopped at step " + i);
            }
        }

        [Test("pointer movement alone is not treated as input")]
        public void PointerMovementIsNotInput()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            // A drifting hand, over a period longer than the hold.
            DateTime t = c;
            for (int i = 0; i < 40; i++)
            {
                _p.DriftPointer();
                _e.Tick(t);
                t = t.AddSeconds(5);
            }
            Assert.Same(WatchState.Cycling, _e.State,
                "drifting the mouse must not stop the cycling");
            Assert.True(_p.Injections > 1, "and it should have carried on switching");
        }

        [Test("a click that does not move the pointer is input")]
        public void ClickWithoutMovementIsInput()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _p.Click();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State,
                "a click leaves the pointer still, which is what tells it apart from drift");
        }

        // ---- the Start/Pause button -----------------------------------------

        [Test("Pause goes to Holding with a Start button")]
        public void PauseEntersHolding()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Equal("Pause", _e.ButtonLabel, "precondition: cycling, so it offers Pause");

            _e.Toggle(c);
            Assert.Same(WatchState.Holding, _e.State, "pause stops the cycling");
            Assert.Equal("Start", _e.ButtonLabel, "and offers Start to resume");
            Assert.Equal("Holding for 60s", _e.StatusText, "with a full delay pending");
        }

        [Test("Pause stays paused however much you type")]
        public void PauseSurvivesTyping()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _e.Toggle(c);

            // The reported bug: typing used to drag the state machine out of the
            // stop, so it resumed on its own.
            DateTime t = c;
            for (int i = 0; i < 10; i++)
            {
                _p.TypeKey();
                _e.Tick(t);
                t = t.AddSeconds(3);
                Assert.Same(WatchState.Holding, _e.State,
                    "must still be paused at step " + i);
            }
            Assert.Equal("Start", _e.ButtonLabel, "and still offer Start");
        }

        [Test("Pause does not fall back to cycling while the hold runs")]
        public void PauseDoesNotFlickerBack()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            int before = _p.Injections;
            _e.Toggle(c);

            // Poll right up to the moment the hold expires.
            Poll(c, Hold, stepMs: 100);
            Assert.Equal(before, _p.Injections, "nothing may switch while the hold runs");
            Assert.Same(WatchState.Holding, _e.State, "still holding one poll before expiry");

            _e.Tick(c.AddSeconds(Hold));
            Assert.Same(WatchState.Cycling, _e.State, "and only then does it resume");
        }

        [Test("Start from Holding skips the hold but still waits an interval")]
        public void StartSkipsHoldButWaitsInterval()
        {
            Setup();
            Assert.Same(WatchState.Holding, _e.State, "precondition: holding at launch");

            _e.Toggle(T0);
            Assert.Same(WatchState.Cycling, _e.State, "Start goes straight to cycling");
            Assert.Equal(0, _p.Injections, "but must not switch on the same instant");

            TickAt(T0, 1, 2, 3, 4);
            Assert.Equal(0, _p.Injections, "still counting down");

            _e.Tick(T0.AddSeconds(5));
            Assert.Equal(1, _p.Injections, "first switch one interval later");
        }

        [Test("the button label always matches what a press would do")]
        public void LabelAndActionAgree()
        {
            // The bug this guards: the label was recomputed on a timer while the
            // click handler decided from live state, so a press could do the
            // opposite of what the button promised.
            WatchState[] states = { WatchState.Away, WatchState.Holding, WatchState.Cycling };
            foreach (WatchState s in states)
            {
                Setup();
                DriveTo(s);
                string label = _e.ButtonLabel;
                WatchState before = _e.State;
                _e.Toggle(T0.AddHours(1));
                WatchState after = _e.State;

                if (label == "Pause")
                    Assert.False(before == after, "a button reading Pause must change the state");
                else
                    Assert.Same(WatchState.Cycling, after,
                        "a button reading Start must end up cycling");
            }
        }

        void DriveTo(WatchState target)
        {
            if (target == WatchState.Away)
            {
                _p.TerminalInFront = false;
                _e.Tick(T0);
                return;
            }
            if (target == WatchState.Holding)
            {
                _e.Tick(T0);
                return;
            }
            _e.Toggle(T0);
        }

        // ---- focus ----------------------------------------------------------

        [Test("leaving the terminal goes Idle and stops switching")]
        public void LeavingTerminalStopsEverything()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            int before = _p.Injections;

            _p.TerminalInFront = false;
            Poll(c.AddSeconds(1), Interval * 3);
            Assert.Same(WatchState.Away, _e.State, "not watching, so Idle");
            Assert.Equal(before, _p.Injections, "and nothing may switch");
            Assert.Equal("Start", _e.ButtonLabel, "offering Start");
        }

        [Test("returning to the terminal starts a fresh hold")]
        public void ReturningStartsHold()
        {
            Setup();
            _p.TerminalInFront = false;
            _e.Tick(T0);
            Assert.Same(WatchState.Away, _e.State, "precondition: away");

            _p.TerminalInFront = true;
            _e.Tick(T0.AddSeconds(5));
            Assert.Same(WatchState.Holding, _e.State, "coming back holds before cycling");
            Assert.Equal("Holding for 60s", _e.StatusText, "with a full delay");
        }

        [Test("the widget's own clicks are not treated as terminal input")]
        public void OwnClicksAreNotInput()
        {
            // The bug this guards: pressing Start is itself a click, so the next
            // poll read it as the user taking over and sprang straight back to
            // Holding.
            Setup();
            _e.WidgetHandle = new IntPtr(2000);
            _e.Toggle(T0);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            // The click that pressed the button, as the form would report it.
            _p.Click();
            _e.NoteOwnInput();

            _e.Tick(T0.AddSeconds(1));
            Assert.Same(WatchState.Cycling, _e.State,
                "pressing the widget must not stop the cycling or re-arm the hold");
            Assert.Equal("Cycling every 5s", _e.StatusText, "still reported as cycling");
        }

        [Test("an injection is not read back as user input")]
        public void InjectionIsNotInput()
        {
            // InjectNextTab bumps the system input stamp in reality, so without
            // recording it the cycler's own keystroke would stop it dead.
            Setup();
            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));                    // first switch
            Assert.Equal(1, _p.Injections, "precondition: it switched once");
            Assert.Same(WatchState.Cycling, _e.State,
                "its own keystroke must not be mistaken for the user taking over");
        }

        [Test("nothing is injected if focus leaves between the check and the send")]
        public void NoInjectionWhenFocusLeaves()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            int before = _p.Injections;

            _p.TerminalInFront = false;
            _e.Tick(c.AddSeconds(Interval));
            Assert.Equal(before, _p.Injections, "must not inject when the terminal is not in front");
        }

        // ---- display --------------------------------------------------------

        [Test("the holding countdown visibly decreases")]
        public void HoldingCountdownDecreases()
        {
            Setup();
            _e.Tick(T0);
            Assert.Equal("Holding for 60s", _e.StatusText, "starts at 60");
            _e.Tick(T0.AddSeconds(30));
            Assert.Equal("Holding for 30s", _e.StatusText, "counts down");
            _e.Tick(T0.AddSeconds(59));
            Assert.Equal("Holding for 1s", _e.StatusText, "and reaches 1");
        }

        [Test("the next-switch countdown shows the interval")]
        public void CyclingCountdownDecreases()
        {
            Setup();
            _e.Toggle(T0);
            Assert.Equal("next tab in 5s", _e.DetailText, "shows the full interval first");
            _e.Tick(T0.AddSeconds(3));
            Assert.Equal("next tab in 2s", _e.DetailText, "counts down to the switch");
        }

        [Test("Idle tells the user what to do")]
        public void IdleExplainsItself()
        {
            Setup();
            _p.TerminalInFront = false;
            _e.Tick(T0);
            Assert.Equal("Idle", _e.StatusText, "says it is idle");
            Assert.Equal("focus Windows Terminal to start", _e.DetailText, "and what would start it");
            Assert.True(_e.ButtonIsHighlighted, "Start is the highlighted action when not cycling");
        }
    }
}
