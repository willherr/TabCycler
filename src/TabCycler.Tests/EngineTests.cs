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

        /// <summary>Setup with non-default input options.</summary>
        void SetupWith(InputOptions options)
        {
            _p = new FakePlatform();
            _log = new LogSink();
            _e = new CyclerEngine(_p, Interval, Hold, T0,
                                 CyclerEngine.DefaultMoveThresholdPixels, options);
            _e.Log = _log.Add;
        }

        static InputOptions OptionsOff(params string[] kinds)
        {
            InputOptions o = InputOptions.Default;
            foreach (string k in kinds)
            {
                if (k == "key") o.ResetOnKeyPress = false;
                if (k == "click") o.ResetOnClick = false;
                if (k == "scroll") o.ResetOnScroll = false;
                if (k == "movement") o.ResetOnMovement = false;
            }
            return o;
        }

        /// <summary>
        /// Drives the engine into Cycling, then fires one input of the given
        /// kind and reports whether the hold was re-armed.
        /// </summary>
        bool ArmsOn(Action fire)
        {
            SetupWith(_options);
            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));
            if (_e.State != WatchState.Cycling) return false;   // precondition failed
            fire();
            _e.Tick(T0.AddSeconds(6));
            return _e.State == WatchState.Holding;
        }

        InputOptions _options = InputOptions.Default;

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
        /// <summary>
        /// Drives the engine to genuinely Cycling and returns the moment it
        /// began. Start no longer lands in Cycling directly, it restarts the
        /// hold for one interval first, so a helper returning the press time
        /// would hand every caller a state that had not arrived yet.
        /// </summary>
        DateTime ReachCyclingStart()
        {
            _e.Toggle(T0);
            return RunUntilCycling(T0);
        }

        /// <summary>
        /// As above, then on to the first tab switch, returning that moment.
        /// A good number of tests are written relative to the first switch, so
        /// this keeps that contract instead of making every caller rediscover
        /// the extra interval that the hold now costs.
        /// </summary>
        DateTime ReachCycling()
        {
            DateTime t = ReachCyclingStart();
            for (int i = 0; i < 200 && _p.Injections == 0; i++)
            {
                t = t.AddMilliseconds(250);
                _e.Tick(t);
            }
            Assert.True(_p.Injections > 0, "precondition: a tab was actually switched");
            return t;
        }

        /// <summary>
        /// Presses Start at a given moment and runs the clock to the point where
        /// cycling has actually begun, for tests that care about what the press
        /// led to rather than the press itself.
        /// </summary>
        DateTime StartAndReachCycling(DateTime from)
        {
            _e.Toggle(from);
            return RunUntilCycling(from);
        }

        DateTime RunUntilCycling(DateTime from)
        {
            DateTime t = from;
            for (int i = 0; i < 200 && _e.State != WatchState.Cycling; i++)
            {
                t = t.AddMilliseconds(250);
                _e.Tick(t);
            }
            Assert.Same(WatchState.Cycling, _e.State,
                "precondition: a Start press must actually reach Cycling");
            return t;
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

        [Test("a deliberate pointer movement stops the cycling")]
        public void DeliberatePointerMovementStopsCycling()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            // A real hand movement, well past the threshold.
            _p.MovePointer(60, 40);
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State,
                "moving the mouse a real distance is deliberate input");
            Assert.Equal("Holding for 60s", _e.StatusText, "and restarts the full hold");
        }

        [Test("movement on either axis alone is enough")]
        public void MovementOnOneAxisCounts()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _p.MovePointer(50, 0);
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State, "horizontal movement alone should count");

            _e.Toggle(c.AddSeconds(2));
            Poll(c.AddSeconds(2), 1);
            _p.MovePointer(0, 50);
            _e.Tick(c.AddSeconds(3));
            Assert.Same(WatchState.Holding, _e.State, "vertical movement alone should count too");
        }

        [Test("jitter below the threshold is ignored, so the countdown still runs")]
        public void SmallJitterIsIgnored()
        {
            // The guard against the original problem, where any movement re-armed
            // the hold every poll and the widget sat at 60 forever.
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            // Sub-threshold twitch every 250ms for well over a minute.
            DateTime t = c;
            for (int i = 0; i < 300; i++)
            {
                _p.DriftPointer();      // +2,+1
                _e.Tick(t);
                t = t.AddMilliseconds(250);
            }
            Assert.Same(WatchState.Cycling, _e.State,
                "a hand resting on the mouse must not hold the widget off forever");
        }

        [Test("the jitter threshold is configurable")]
        public void ThresholdIsConfigurable()
        {
            _p = new FakePlatform();
            _e = new CyclerEngine(_p, Interval, Hold, T0, 50);
            Assert.Equal(50, _e.MoveThresholdPixels, "the threshold is the one supplied");

            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));            // cycling
            _p.MovePointer(30, 0);                // below the raised threshold
            _e.Tick(T0.AddSeconds(6));
            Assert.Same(WatchState.Cycling, _e.State,
                "with a high threshold, 30px no longer counts as deliberate");

            _p.MovePointer(60, 0);                // above it
            _e.Tick(T0.AddSeconds(7));
            Assert.Same(WatchState.Holding, _e.State, "60px does");
        }

        [Test("rejects a movement threshold below one pixel")]
        public void RejectsBadThreshold()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CyclerEngine(new FakePlatform(), Interval, Hold, T0, 0),
                "a zero threshold would make every pixel count and reintroduce the stuck countdown");
        }

        [Test("scrolling stops the cycling")]
        public void ScrollStopsCycling()
        {
            // A wheel event changes the input stamp but leaves the pointer
            // exactly where it was, which is how it is told apart from nothing.
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            _p.ScrollWheel();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State, "a wheel event is deliberate input");
            Assert.Equal("Holding for 60s", _e.StatusText, "and restarts the full hold");
        }

        [Test("scrolling keeps the hold from expiring while it continues")]
        public void ContinuousScrollingKeepsHolding()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _p.ScrollWheel();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State, "precondition: held by the scroll");

            DateTime t = c.AddSeconds(1);
            for (int i = 0; i < 20; i++)
            {
                _p.ScrollWheel();
                _e.Tick(t);
                t = t.AddSeconds(5);
                Assert.Same(WatchState.Holding, _e.State,
                    "scrolling for longer than the hold should keep it held at step " + i);
            }
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

        [Test("Pause parks in Paused with a Start button")]
        public void PauseEntersPaused()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Equal("Pause", _e.ButtonLabel, "precondition: cycling, so it offers Pause");

            _e.Toggle(c);
            Assert.Same(WatchState.Paused, _e.State, "pause stops the cycling");
            Assert.Equal("Start", _e.ButtonLabel, "and offers Start to resume");
            Assert.Equal("Paused", _e.StatusText,
                "no countdown, because nothing is counting down");
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
                Assert.Same(WatchState.Paused, _e.State,
                    "must still be paused at step " + i);
            }
            Assert.Equal("Start", _e.ButtonLabel, "and still offer Start");
        }

        [Test("Pause stays paused until Start, however long that takes")]
        public void PauseLatchesUntilStart()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            int before = _p.Injections;

            _e.Toggle(c);
            Assert.Same(WatchState.Paused, _e.State, "the press pauses immediately");

            // Far longer than the resume delay. Pause used to set the same
            // deadline a keystroke does, so the widget quietly started cycling
            // again after the hold, which is not what Pause means.
            Poll(c, Hold * 5, stepMs: 100);
            Assert.Equal(before, _p.Injections, "nothing may switch while paused");
            Assert.Same(WatchState.Paused, _e.State,
                "and it is still paused well past where the old hold would have ended");

            _e.Toggle(c.AddSeconds(Hold * 5));
            Assert.Same(WatchState.Holding, _e.State,
                "Start restarts the hold rather than snapping to cycling");
            StartAndReachCyclingFrom(c.AddSeconds(Hold * 5));
            Assert.Same(WatchState.Cycling, _e.State, "and it does lead back to cycling");
        }

        /// <summary>
        /// Drives the clock on from a moment at which Start has just been
        /// pressed, until cycling has actually begun.
        /// </summary>
        void StartAndReachCyclingFrom(DateTime from)
        {
            for (int i = 0; i < 200 && _e.State != WatchState.Cycling; i++)
                _e.Tick(from = from.AddMilliseconds(250));
            Assert.Same(WatchState.Cycling, _e.State, "Start must lead to cycling");
        }

        [Test("a pause survives stray input and losing focus")]
        public void PauseSurvivesInputAndFocus()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _e.Toggle(c);
            Assert.Same(WatchState.Paused, _e.State, "precondition: paused");

            // Input that would normally re-arm the hold.
            _p.TypeKey();
            _p.Click();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Paused, _e.State, "a keystroke does not undo a pause");

            // Focus leaving the terminal would normally mean Idle.
            _p.TerminalInFront = false;
            _e.Tick(c.AddSeconds(2));
            Assert.Same(WatchState.Paused, _e.State, "losing focus does not undo a pause");
            Assert.False(_e.ShouldBeTopMost,
                "but it stops floating over other windows while the terminal is away");

            // And the pause is still in force when the terminal comes back.
            _p.TerminalInFront = true;
            _e.Tick(c.AddSeconds(3));
            Assert.Same(WatchState.Paused, _e.State, "regaining focus does not undo a pause");

            _e.Toggle(c.AddSeconds(4));
            Assert.Same(WatchState.Holding, _e.State,
                "Start from a pause shows the hold, and the pause is genuinely over");
            StartAndReachCyclingFrom(c.AddSeconds(4));
            Assert.Same(WatchState.Cycling, _e.State, "and it cycles once started again");
        }

        [Test("Start restarts the hold for one cycle interval")]
        public void StartRestartsTheHoldForOneInterval()
        {
            Setup();
            Assert.Same(WatchState.Holding, _e.State, "precondition: holding at launch");

            _e.Toggle(T0);
            Assert.Same(WatchState.Holding, _e.State,
                "Start shows the hold again rather than snapping to Cycling");
            Assert.Equal(Interval + "s", _e.StatusText.Substring("Holding for ".Length),
                "and the hold is one cycle interval, not the 60s resume delay");
            Assert.Equal(0, _p.Injections, "nothing may switch during the hold");

            TickAt(T0, 1, 2, 3, 4);
            Assert.Same(WatchState.Holding, _e.State, "still holding one poll before expiry");
            Assert.Equal(0, _p.Injections, "and still nothing switched");

            _e.Tick(T0.AddSeconds(Interval));
            Assert.Same(WatchState.Cycling, _e.State, "the hold elapses into cycling");

            _e.Tick(T0.AddSeconds(Interval + 1));
            Assert.Equal(0, _p.Injections, "the first switch waits a further interval");

            _e.Tick(T0.AddSeconds(Interval * 2));
            Assert.Equal(1, _p.Injections, "so nothing is flipped the instant Start is pressed");
        }

        [Test("Start from Paused also goes through the hold")]
        public void StartFromPausedShowsTheHold()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            _e.Toggle(c);
            Assert.Same(WatchState.Paused, _e.State, "precondition: paused");

            _e.Toggle(c.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State,
                "resuming from a pause shows the countdown rather than jumping to cycling");
            Assert.Equal("Start", _e.ButtonLabel, "and still offers Start while it waits");
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
                    Assert.Same(WatchState.Holding, after,
                        "a button reading Start shows the hold again, not a jump to cycling");
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
            DateTime c = StartAndReachCycling(T0);
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            // The click that pressed the button, as the form would report it.
            _p.Click();
            _e.NoteOwnInput();

            _e.Tick(c.AddSeconds(1));
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
            _e.Tick(T0.AddSeconds(5));                    // the hold elapses
            _e.Tick(T0.AddSeconds(10));                   // first switch
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
            DateTime c = ReachCyclingStart();
            Assert.Equal("next tab in 5s", _e.DetailText, "shows the full interval first");
            _e.Tick(c.AddSeconds(3));
            Assert.Equal("next tab in 2s", _e.DetailText, "counts down to the switch");
        }

        [Test("the holding hint mentions every kind of input")]
        public void HoldingHintCoversAllInputs()
        {
            Setup();
            _e.Tick(T0);
            Assert.Equal("reset by typing, clicks, scroll or movement", _e.DetailText,
                "the hint should tell the user everything that re-arms the hold");
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

        // ---- per-kind input filtering, which the hook makes possible --------

        [Test("each input kind stops the cycling by default")]
        public void EveryKindStopsByDefault()
        {
            Assert.True(ArmsOn(() => _p.TypeKey()), "typing stops it");
            Assert.True(ArmsOn(() => _p.Click()), "clicking stops it");
            Assert.True(ArmsOn(() => _p.ScrollWheel()), "scrolling stops it");
            Assert.True(ArmsOn(() => _p.Sweep(120, 0, 12)), "moving the pointer stops it");
        }

        [Test("typing can be switched off without affecting the others")]
        public void TypingCanBeDisabled()
        {
            _options = OptionsOff("key");
            Assert.False(ArmsOn(() => _p.TypeKey()), "typing must be ignored");
            Assert.True(ArmsOn(() => _p.Click()), "clicking still counts");
            Assert.True(ArmsOn(() => _p.ScrollWheel()), "scrolling still counts");
        }

        [Test("clicking can be switched off without affecting the others")]
        public void ClickingCanBeDisabled()
        {
            _options = OptionsOff("click");
            Assert.False(ArmsOn(() => _p.Click()), "clicking must be ignored");
            Assert.True(ArmsOn(() => _p.TypeKey()), "typing still counts");
        }

        [Test("scrolling can be switched off without affecting the others")]
        public void ScrollingCanBeDisabled()
        {
            _options = OptionsOff("scroll");
            Assert.False(ArmsOn(() => _p.ScrollWheel()), "scrolling must be ignored");
            Assert.True(ArmsOn(() => _p.TypeKey()), "typing still counts");
        }

        [Test("movement can be switched off without affecting the others")]
        public void MovementCanBeDisabled()
        {
            _options = OptionsOff("movement");
            Assert.False(ArmsOn(() => _p.Sweep(120, 0, 12)), "movement must be ignored");
            Assert.True(ArmsOn(() => _p.TypeKey()), "typing still counts");
        }

        [Test("all four can be switched off, leaving input to never hold")]
        public void EverythingCanBeDisabled()
        {
            _options = OptionsOff("key", "click", "scroll", "movement");
            Assert.False(ArmsOn(() => _p.TypeKey()), "typing ignored");
            Assert.False(ArmsOn(() => _p.Click()), "clicking ignored");
            Assert.False(ArmsOn(() => _p.ScrollWheel()), "scrolling ignored");
            Assert.False(ArmsOn(() => _p.Sweep(120, 0, 12)), "movement ignored");
        }

        // ---- synthetic input, the reason the hook exists ---------------------

        [Test("input injected by another process is ignored by default")]
        public void InjectedInputIsIgnoredByDefault()
        {
            // This is issue #7. A browser-automation agent driving the real
            // mouse must not read as the user, or the hold never expires.
            _options = InputOptions.Default;
            Assert.False(ArmsOn(() => _p.InjectedKey()), "an injected keystroke must not arm it");
            Assert.False(ArmsOn(() => _p.InjectedMovement(200, 200)), "nor injected movement");
        }

        [Test("injected input can be made to count, for anyone who wants that")]
        public void InjectedInputCanBeCounted()
        {
            _options = InputOptions.Default;
            _options.IgnoreInjected = false;
            Assert.True(ArmsOn(() => _p.InjectedKey()),
                "with the option off, a synthetic keystroke counts like any other");
        }

        [Test("real input still counts while injected input is ignored")]
        public void RealInputStillCountsWithIgnoreInjected()
        {
            _options = InputOptions.Default;   // IgnoreInjected = true
            Assert.True(ArmsOn(() => _p.TypeKey()), "real typing must still arm it");
        }

        [Test("an unclassified event counts if any of the three it could be is enabled")]
        public void UnclassifiedRespectsAnyEnabled()
        {
            // The fallback path when no hook is available can only say "one of
            // these three happened". Arming on any enabled one is the honest
            // reading, and it must not arm when all three are off.
            SetupWith(OptionsOff("key"));
            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");
            _p.PushUnclassified();
            _e.Tick(T0.AddSeconds(6));
            Assert.Same(WatchState.Holding, _e.State,
                "clicking is still enabled, so an unclassified event should arm it");

            SetupWith(OptionsOff("key", "click", "scroll"));
            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));
            _p.PushUnclassified();
            _e.Tick(T0.AddSeconds(6));
            Assert.Same(WatchState.Cycling, _e.State,
                "with all three off, an unclassified event must not arm it");
        }

        [Test("InputOptions equality and defaults are sane")]
        public void InputOptionsDefaultsAndEquality()
        {
            Assert.Equal(InputOptions.Default, InputOptions.Default, "default compares equal");
            InputOptions off = InputOptions.Default;
            off.ResetOnKeyPress = false;
            Assert.False(InputOptions.Default.Equals(off), "a changed flag is a different value");
            Assert.True(InputOptions.Default.IgnoreInjected,
                "synthetic input is ignored out of the box, which is issue #7's fix");
            Assert.True(InputOptions.Default.ArmsFor(InputKind.Unclassified),
                "unclassified arms when any of its three candidates is enabled");
        }

        // ---- live settings, which is what the dialog in #9 depends on --------

        [Test("edited timings take effect on the next tick without a restart")]
        public void ApplySettingsChangesInterval()
        {
            Setup();
            DateTime c = ReachCycling();
            int before = _p.Injections;

            _e.ApplySettings(30, Hold, CyclerEngine.DefaultMoveThresholdPixels, InputOptions.Default);
            Assert.Equal(30, _e.IntervalSeconds, "the new interval is readable");

            // Still inside the old 5s wait, which must not now fire.
            _e.Tick(c.AddSeconds(1));
            Assert.Equal(before, _p.Injections, "the old short interval no longer applies");

            _e.Tick(c.AddSeconds(31));
            Assert.Equal(before + 1, _p.Injections, "and the new long one does");
        }

        [Test("a longer interval does not fire early just because the old deadline passed")]
        public void ApplySettingsReschedulesRatherThanKeepingPhase()
        {
            Setup();
            DateTime c = ReachCycling();
            int before = _p.Injections;

            // The next switch is now c+5. Change the interval at c+1, when that
            // deadline is already set.
            _e.ApplySettings(60, Hold, CyclerEngine.DefaultMoveThresholdPixels, InputOptions.Default);

            _e.Tick(c.AddSeconds(20));
            Assert.Equal(before, _p.Injections,
                "preserving the old phase would have switched here, which is surprising");
            _e.Tick(c.AddSeconds(62));
            Assert.Equal(before + 1, _p.Injections, "and the new interval still fires");
        }

        [Test("a new resume delay applies from the moment of the change")]
        public void ApplySettingsReschedulesTheHold()
        {
            Setup();
            // Idle at the terminal on start, so the 60s hold is already running.
            _e.Tick(T0.AddSeconds(1));
            _e.ApplySettings(Interval, 5, CyclerEngine.DefaultMoveThresholdPixels, InputOptions.Default);

            _e.Tick(T0.AddSeconds(7));
            Assert.Same(WatchState.Cycling, _e.State,
                "a 5s hold starting at t=1 is up at t=6, so cycling has begun");
        }

        [Test("turning an input kind off while running stops it arming")]
        public void ApplyOptionsMidRun()
        {
            Setup();
            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));
            Assert.Same(WatchState.Cycling, _e.State, "precondition: cycling");

            _e.ApplySettings(Interval, Hold, CyclerEngine.DefaultMoveThresholdPixels,
                             OptionsOff("key"));
            _p.TypeKey();
            _e.Tick(T0.AddSeconds(6));
            Assert.Same(WatchState.Cycling, _e.State,
                "typing no longer arms, because the dialog turned it off");

            _e.ApplySettings(Interval, Hold, CyclerEngine.DefaultMoveThresholdPixels,
                             InputOptions.Default);
            _p.TypeKey();
            _e.Tick(T0.AddSeconds(7));
            Assert.Same(WatchState.Holding, _e.State,
                "turning it back on arms again without a restart");
        }

        [Test("the movement threshold can be changed while running")]
        public void ApplySettingsChangesThreshold()
        {
            Setup();
            DateTime c = ReachCycling();

            // DriftPointer moves 2px, which is under the 8px default and over 1.
            _p.DriftPointer();
            _e.Tick(c.AddSeconds(1));
            Assert.Same(WatchState.Cycling, _e.State, "precondition: 2px is under 8");

            _e.ApplySettings(Interval, Hold, 1, InputOptions.Default);
            Assert.Equal(1, _e.MoveThresholdPixels, "the new threshold is readable");

            _e.Tick(c.AddSeconds(2));
            _p.DriftPointer();
            _e.Tick(c.AddSeconds(3));
            Assert.Same(WatchState.Holding, _e.State, "a 2px nudge now counts");
        }

        [Test("a bad setting is rejected and changes nothing")]
        public void ApplySettingsRejectsBadValues()
        {
            Setup();
            _e.Toggle(T0);
            _e.Tick(T0.AddSeconds(5));

            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { _e.ApplySettings(0, Hold, 8, InputOptions.Default); },
                "a zero interval is refused");
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { _e.ApplySettings(Interval, -1, 8, InputOptions.Default); },
                "a negative hold is refused");
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { _e.ApplySettings(Interval, Hold, 0, InputOptions.Default); },
                "a zero threshold is refused");

            Assert.Equal(Interval, _e.IntervalSeconds, "the interval survived the rejection");
            Assert.Equal(Hold, _e.HoldSeconds, "the hold survived the rejection");
            Assert.Equal(CyclerEngine.DefaultMoveThresholdPixels, _e.MoveThresholdPixels,
                "the threshold survived the rejection");
            Assert.Same(WatchState.Cycling, _e.State, "and the engine kept running");
        }

        [Test("a rejected setting does not reschedule the pending wait")]
        public void ApplySettingsRejectionIsAtomic()
        {
            Setup();
            DateTime c = ReachCycling();
            int before = _p.Injections;

            try
            {
                _e.ApplySettings(60, Hold, 0, InputOptions.Default);
                Assert.True(false, "expected the call to throw");
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            // The original 5s interval still governs, so the switch lands where
            // it would have without the rejected call.
            _e.Tick(c.AddSeconds(5));
            Assert.Equal(before + 1, _p.Injections,
                "the deadline was left alone, so the original 5s interval still governs");
        }

        // ---- the hint line, which now describes whatever is actually enabled --

        [Test("the hint names every kind that is switched on")]
        public void DetailTextListsEnabledKinds()
        {
            SetupWith(InputOptions.Default);
            _e.Tick(T0.AddSeconds(1));
            Assert.Same(WatchState.Holding, _e.State, "precondition: holding");
            Assert.Equal("reset by typing, clicks, scroll or movement", _e.DetailText,
                "all four kinds are on by default");
        }

        [Test("the hint drops a kind once it is switched off")]
        public void DetailTextFollowsSettings()
        {
            Setup();
            _e.Tick(T0.AddSeconds(1));
            _e.ApplySettings(Interval, Hold, CyclerEngine.DefaultMoveThresholdPixels,
                             OptionsOff("movement", "click"));
            Assert.Equal("reset by typing or scroll", _e.DetailText,
                "the hint must not claim clicks or movement once they are off");
        }

        [Test("the hint admits when nothing can re-arm the hold")]
        public void DetailTextWithNothingEnabled()
        {
            Setup();
            _e.Tick(T0.AddSeconds(1));
            _e.ApplySettings(Interval, Hold, CyclerEngine.DefaultMoveThresholdPixels,
                             OptionsOff("key", "click", "scroll", "movement"));
            Assert.Equal("reset by nothing, the wait always runs", _e.DetailText,
                "an empty list would be a worse answer than saying so");
        }

        [Test("the input summary in the log is the same string the widget builds")]
        public void DescribeInputIsShared()
        {
            InputOptions o = OptionsOff("scroll", "movement");
            Assert.Equal("key,click,skipInjected", CyclerEngine.DescribeInput(o),
                "one definition, so the log and the widget cannot disagree");

            // Ignoring injected input is independent of which kinds arm the
            // hold, so switching the four kinds off still leaves it reported.
            InputOptions off = OptionsOff("key", "click", "scroll", "movement");
            Assert.Equal("skipInjected", CyclerEngine.DescribeInput(off),
                "a filter that is still on is still reported");

            off.IgnoreInjected = false;
            Assert.Equal("none", CyclerEngine.DescribeInput(off),
                "only with nothing left at all is it 'none'");
        }

        // ---- floating above other windows only when it is useful --------------

        [Test("the widget only floats above other apps while the terminal is in front")]
        public void TopMostFollowsTerminalFocus()
        {
            Setup();
            _e.Tick(T0);
            Assert.True(_e.ShouldBeTopMost, "terminal in front, so it stays in front");

            _p.TerminalInFront = false;
            _e.Tick(T0.AddSeconds(1));
            Assert.Same(WatchState.Away, _e.State, "precondition: the terminal went away");
            Assert.False(_e.ShouldBeTopMost,
                "over the browser is exactly when the widget is only in the way");

            _p.TerminalInFront = true;
            _e.Tick(T0.AddSeconds(2));
            Assert.True(_e.ShouldBeTopMost, "and it comes back when the terminal does");
        }

        [Test("AlwaysOnTop restores the unconditional behaviour")]
        public void AlwaysOnTopOverridesFocus()
        {
            Setup();
            _p.TerminalInFront = false;
            _e.Tick(T0);
            Assert.False(_e.ShouldBeTopMost, "precondition: not topmost with the terminal away");

            _e.AlwaysOnTop = true;
            Assert.True(_e.ShouldBeTopMost, "the setting pins it regardless");

            _e.AlwaysOnTop = false;
            Assert.False(_e.ShouldBeTopMost, "and turning it off takes effect immediately");
        }

        [Test("AlwaysOnTop is off by default")]
        public void AlwaysOnTopDefaultsOff()
        {
            Setup();
            Assert.False(_e.AlwaysOnTop,
                "defaulting it on would put the widget over every app, which was the complaint");
        }

        [Test("the button offers Start while paused, and the status says so")]
        public void PausedIsLabelledHonestly()
        {
            Setup();
            DateTime c = ReachCycling();
            Poll(c, 1);
            Assert.Equal("Pause", _e.ButtonLabel, "precondition: cycling offers Pause");

            _e.Toggle(c);
            Assert.Same(WatchState.Paused, _e.State, "precondition: paused");
            Assert.Equal("Start", _e.ButtonLabel, "the only way out is offered");
            Assert.True(_e.ButtonIsHighlighted, "and it is highlighted, because it is what to do");
            Assert.Equal("Paused", _e.StatusText, "the status does not imply a countdown");
            Assert.Equal("press Start to cycle again", _e.DetailText,
                "and the hint says how to get out");
        }
    }
}
