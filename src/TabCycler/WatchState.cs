namespace TabCycler
{
    /// <summary>
    /// The states the cycler can be in.
    ///
    /// There used to be three, with "stopped" having a single spelling: pausing,
    /// going idle after returning, and reacting to input all landed in Holding.
    /// That was written to stop a bool and the state enum disagreeing with each
    /// other, which was a real bug. But it conflated two different things: a
    /// timed wait that ends by itself, and a deliberate pause that the user
    /// expects to last until they say otherwise. Folding Pause into Holding meant
    /// Pause silently restarted after the resume delay, which is not what anyone
    /// means by Pause.
    ///
    /// So Paused is a state in its own right. The rule that was violated is not
    /// "never add a fourth state" but "never have two ways to say stopped" --
    /// so nothing else is allowed to mean paused, and in particular a bool
    /// alongside this enum would reintroduce exactly the disagreement that rule
    /// was written to prevent.
    /// </summary>
    public enum WatchState
    {
        /// <summary>Windows Terminal is not in front: nothing to do.</summary>
        Away,

        /// <summary>
        /// Not cycling, waiting out the resume delay. Reached by user input
        /// while cycling and by returning to the terminal. Always ends by
        /// itself, either because the delay elapsed or because there was more
        /// input.
        /// </summary>
        Holding,

        /// <summary>Actively switching tabs on the interval.</summary>
        Cycling,

        /// <summary>
        /// Stopped deliberately and until the user says otherwise. Survives
        /// focus changes and stray input, because a pause that a background
        /// process can undo is not a pause.
        /// </summary>
        Paused
    }

    /// <summary>
    /// Pointer position, as a plain value. Kept free of System.Drawing so the
    /// engine has no dependency beyond the core library.
    /// </summary>
    public struct CursorPos
    {
        public readonly int X;
        public readonly int Y;

        public CursorPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool SameAs(CursorPos other)
        {
            return X == other.X && Y == other.Y;
        }
    }
}
