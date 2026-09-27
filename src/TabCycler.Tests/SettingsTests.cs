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
            Assert.False(s.AlwaysOnTop,
                "always-on-top is off by default, because the widget floats itself "
                + "only while the terminal is in front");
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
            first.AlwaysOnTop = true;
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
            Assert.True(second.AlwaysOnTop, "always-on-top can be turned on");
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

        [Test("a truncated file keeps the settings it still contains")]
        public void TruncatedFileKeepsWhatItHas()
        {
            // The bug this documents: LoadFrom starts from the compiled-in
            // defaults and only overrides the keys it finds. A half-written
            // file therefore does not fail, it silently reverts every key it
            // lost, and the next save makes that permanent. Save is now atomic
            // so this state should not be reachable, but the parse must still
            // not turn a prefix of the file into a total reset.
            Settings s = From("IntervalSeconds=2\nResumeDelay");
            Assert.Equal(2, s.IntervalSeconds, "the complete key before the cut is honoured");
            Assert.Equal(60, s.ResumeDelaySeconds,
                "a key that was cut mid-line falls back, which is the one thing "
                + "atomic writes exist to prevent");
            Assert.Equal(8, s.MoveThresholdPixels, "and so does a key after the cut");
        }

        [Test("save is atomic and leaves no partial or stray file")]
        public void SaveIsAtomic()
        {
            // Against a temporary file, not the real settings: the point is how
            // SaveTo writes, not the text, and a test that wrote to the live
            // settings.txt would clobber whatever the widget is using.
            string dir = Path.Combine(Path.GetTempPath(),
                "TabCyclerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "settings.txt");
                File.WriteAllText(path, "# old contents\nIntervalSeconds=2\n");

                Settings s = Settings.Defaults();
                s.IntervalSeconds = 7;
                s.SaveTo(path);

                string written = File.ReadAllText(path);
                Assert.True(written.Contains("IntervalSeconds=7"),
                    "the swap landed, so the new contents are on disk");
                Assert.False(written.Contains("old contents"),
                    "and none of the previous contents survived");
                Assert.Equal(false, File.Exists(path + ".tmp"),
                    "the temporary file it was written through is gone, not left "
                    + "to be mistaken for the settings later");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
        [Test("a value written to disk is read back on the next construction")]
        public void DiskRoundTrip()
        {
            // This is the test that was missing, and its absence is why a bug in
            // the disk path survived: every other test drives LoadFrom with a
            // string, so the code that actually opens settings.txt on launch was
            // never executed by the suite. It was wrapping the file's contents
            // in a StreamReader, whose constructor wants a path, so it threw on
            // every launch and the widget ran on defaults.
            string dir = Path.Combine(Path.GetTempPath(),
                "TabCyclerTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Settings first = new Settings(dir);
                first.IntervalSeconds = 2;
                first.ResumeDelaySeconds = 15;
                first.ResetOnMovement = false;
                first.AlwaysOnTop = true;
                first.Left = 1234;
                first.Top = 56;
                first.Save();

                Settings second = new Settings(dir);
                Assert.Equal(2, second.IntervalSeconds, "the interval survives a real disk round trip");
                Assert.Equal(15, second.ResumeDelaySeconds, "and so does the resume delay");
                Assert.False(second.ResetOnMovement, "and a boolean");
                Assert.True(second.AlwaysOnTop, "and another boolean");
                Assert.Equal(1234, second.Left, "and the position");
                Assert.Equal(56, second.Top, "and the other position axis");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test("a missing file yields defaults instead of throwing")]
        public void MissingFileYieldsDefaults()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "TabCyclerTests_" + Guid.NewGuid().ToString("N"));
            Settings s = new Settings(dir);
            Assert.Equal(5, s.IntervalSeconds, "no file means the compiled-in default");
            Assert.Equal(60, s.ResumeDelaySeconds, "for every value");
        }
    }
}
