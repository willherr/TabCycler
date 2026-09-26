namespace TabCycler
{
    /// <summary>
    /// The three states the cycler can be in. "Stopped" deliberately has a
    /// single spelling: pausing, going idle after returning, and reacting to
    /// input all land in Holding. An earlier design had a bool alongside the
    /// state enum and then a fourth state, and the two spellings could disagree
    /// with each other, so a button press did the opposite of what its label
    /// promised.
    /// </summary>
    public enum WatchState
    {
        /// <summary>Windows Terminal is not in front: nothing to do.</summary>
        Away,

        /// <summary>
        /// Not cycling, waiting out the resume delay. Reached by pausing, by
        /// user input while cycling, and by returning to the terminal.
        /// </summary>
        Holding,

        /// <summary>Actively switching tabs on the interval.</summary>
        Cycling
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
