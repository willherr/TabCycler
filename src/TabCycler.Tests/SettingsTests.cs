using System;
using System.IO;

namespace TabCycler.Tests
{
    /// <summary>
    /// Settings has no test coverage at all until now, which was acceptable
    /// while the only way to change a setting was editing the file by hand. The
    /// settings dialog makes it a real surface, so it gets real tests.
    ///
    /// These use <see cref="Settings.LoadFrom"/> and
    /// <see cref="Settings.Serialize"/> rather than the file, so running them
    /// can never overwrite the settings the widget is actually using.
    /// </summary>
    public sealed class SettingsTests
    {
        static Settings From(string text)
        {
            Settings s = Settings.Defaults();
            using (StringReader r = new StringReader(text))
                s.LoadFrom(r);
            return s;
        }

        [Test("defaults are sane when there is no file at all")]
        public void Defaults()
        {
            Settings s = Settings.Defaults();
            Assert.Equal(5, s.IntervalSeconds, "default interval");
            Assert.Equal(60, s.ResumeDelaySeconds, "default resume delay");
            Assert.Equal(8, s.MoveThresholdPixels, "default movement threshold");
            Assert.True(s.ResetOnKeyPress, "typing arms by default");
            Assert.True(s.ResetOnClick, "clicking arms by default");
            Assert.True(s.ResetOnScroll, "scrolling arms by default");
            Assert.True(s.ResetOnMovement, "movement arms by default");
            Assert.True(s.IgnoreInjected, "injected input is ignored by default");
        }

        [Test("every value round-trips through save and load")]
        public void RoundTrip()
        {
            Settings first = Settings.Defaults();
            first.IntervalSeconds = 17;
            first.ResumeDelaySeconds = 0;
            first.MoveThresholdPixels = 3;
            first.ResetOnKeyPress = false;
            first.ResetOnClick = false;
            first.ResetOnScroll = false;
            first.ResetOnMovement = false;
            first.IgnoreInjected = false;
            first.Left = -1440;
            first.Top = 25;

            Settings second = From(first.Serialize());

            Assert.Equal(17, second.IntervalSeconds, "interval survives");
            Assert.Equal(0, second.ResumeDelaySeconds, "a zero delay survives");
            Assert.Equal(3, second.MoveThresholdPixels, "threshold survives");
            Assert.False(second.ResetOnKeyPress, "typing can be turned off");
            Assert.False(second.ResetOnClick, "clicking can be turned off");
            Assert.False(second.ResetOnScroll, "scrolling can be turned off");
            Assert.False(second.ResetOnMovement, "movement can be turned off");
            Assert.False(second.IgnoreInjected, "injected filtering can be turned off");
            Assert.Equal(-1440, second.Left, "a negative position survives");
            Assert.True(second.SeenLeft, "position is marked as seen");
            Assert.Equal(25, second.Top, "top survives");
        }

        [Test("a partial file only overrides the keys it mentions")]
        public void PartialFile()
        {
            Settings s = From("IntervalSeconds=9\n");
            Assert.Equal(9, s.IntervalSeconds, "the one key is applied");
            Assert.Equal(60, s.ResumeDelaySeconds, "the rest keep their defaults");
        }

        [Test("comments, blank lines and stray text are ignored")]
        public void JunkIsIgnored()
        {
            Settings s = From(
                "# a comment\n" +
                "\n" +
                "   \n" +
                "this line has no equals sign\n" +
                "IntervalSeconds=11\n" +
                "=novalue\n" +
                "IntervalSeconds=\n");
            Assert.Equal(11, s.IntervalSeconds, "only the valid assignment counts");
        }

        [Test("out of range numbers are rejected rather than applied")]
        public void OutOfRange()
        {
            Settings s = From(
                "IntervalSeconds=0\n" +
                "IntervalSeconds=-4\n" +
                "IntervalSeconds=notanumber\n" +
                "ResumeDelaySeconds=-1\n" +
                "MoveThresholdPixels=0\n");
            Assert.Equal(5, s.IntervalSeconds, "a zero or negative interval is refused");
            Assert.Equal(60, s.ResumeDelaySeconds, "a negative delay is refused");
            Assert.Equal(8, s.MoveThresholdPixels, "a zero threshold is refused");
        }

        [Test("booleans accept the spellings a person would type")]
        public void LenientBooleans()
        {
            Settings s = From(
                "ResetOnKeyPress=false\n" +
                "ResetOnClick=0\n" +
                "ResetOnScroll=no\n" +
                "ResetOnMovement=FALSE\n" +
                "IgnoreInjected=Yes\n" +
                "ResetOnKeyPress=bogus\n");
            Assert.False(s.ResetOnKeyPress, "'false' works");
            Assert.False(s.ResetOnClick, "'0' works");
            Assert.False(s.ResetOnScroll, "'no' works");
            Assert.False(s.ResetOnMovement, "case does not matter");
            Assert.True(s.IgnoreInjected, "'Yes' works");
            Assert.False(s.ResetOnKeyPress,
                "an unrecognised value keeps the value already in effect");
        }

        [Test("the file is written with a trailing newline and no stray keys")]
        public void SerializeShape()
        {
            Settings s = Settings.Defaults();
            string text = s.Serialize();
            Assert.True(text.EndsWith(Environment.NewLine),
                "a trailing newline keeps the file append-friendly");
            Assert.False(text.Contains("\n\n"),
                "no doubled blank lines, so re-reading is idempotent");
        }

        [Test("loading twice is idempotent, so a rewrite does not drift")]
        public void Idempotent()
        {
            Settings once = From("IntervalSeconds=13\nResumeDelaySeconds=4\n");
            Settings twice = From(once.Serialize());
            Assert.Equal(once.Serialize(), twice.Serialize(),
                "writing then reading changes nothing");
        }
    }
}
