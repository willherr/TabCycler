using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TabCycler
{
    /// <summary>
    /// Reads and writes the small key=value settings file. Timings live here
    /// rather than being compiled in, so they are tweakable without a rebuild.
    /// </summary>
    internal sealed class Settings
    {
        public int IntervalSeconds = 5;
        public int ResumeDelaySeconds = 60;
        public int MoveThresholdPixels = CyclerEngine.DefaultMoveThresholdPixels;
        public bool ResetOnKeyPress = true;
        public bool ResetOnClick = true;
        public bool ResetOnScroll = true;
        public bool ResetOnMovement = true;
        public bool IgnoreInjected = true;
        public bool AlwaysOnTop;
        public int Left, Top;
        public bool SeenLeft, SeenTop;

        /// <summary>The input half of the settings, for the engine.</summary>
        public InputOptions ToInputOptions()
        {
            InputOptions o = new InputOptions();
            o.ResetOnKeyPress = ResetOnKeyPress;
            o.ResetOnClick = ResetOnClick;
            o.ResetOnScroll = ResetOnScroll;
            o.ResetOnMovement = ResetOnMovement;
            o.IgnoreInjected = IgnoreInjected;
            return o;
        }

        internal static string DefaultDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TabCycler");
            }
        }

        readonly string _dir;

        /// <summary>The directory this instance reads and writes.</summary>
        public string Dir { get { return _dir; } }

        string FilePath
        {
            get { return Path.Combine(_dir, "settings.txt"); }
        }

        /// <summary>
        /// Loads the real settings file. Pass a directory to read somewhere
        /// else, which is how the tests exercise the whole disk round-trip
        /// rather than just the parser.
        /// </summary>
        public Settings(string dir = null)
        {
            _dir = dir ?? DefaultDir;
            LoadFromDisk();
        }

        Settings(bool loadFromDisk, string dir = null)
        {
            _dir = dir ?? DefaultDir;
            if (loadFromDisk) LoadFromDisk();
        }

        /// <summary>
        /// A Settings holding only the compiled-in defaults, with no file read.
        /// The public constructor reads settings.txt, so a test using it against
        /// the real location would inherit whatever the widget last saved. This
        /// is the hermetic way in.
        /// </summary>
        public static Settings Defaults()
        {
            return new Settings(false);
        }

        /// <summary>
        /// Reads the settings file, falling back to defaults on any problem. A
        /// corrupt or unreadable file must degrade to working behaviour, never to
        /// a widget that will not start.
        ///
        /// Note the bare StreamReader over the path. This used to be
        /// <c>new StreamReader(File.ReadAllText(FilePath))</c>, which wraps the
        /// file's contents in a constructor that wants a path, so it threw
        /// "Illegal characters in path" on every single launch. The catch below
        /// swallowed that into Debug, which is invisible in a release build, so
        /// the widget silently ran on compiled-in defaults and then saved those
        /// defaults over the user's file on the next write. That is how a
        /// hand-set 15s kept reverting to 60s, and it survived a round of tests
        /// because every other test drives LoadFrom directly and never touches
        /// this method. The failure is now reported through OnLoadFailed so it
        /// lands in the log a user can actually read.
        /// </summary>
        void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                using (StreamReader r = new StreamReader(FilePath))
                    LoadFrom(r);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings read failed, using defaults: " + ex);
                if (OnLoadFailed != null) OnLoadFailed(FilePath, ex);
            }
        }

        /// <summary>
        /// Reports a failed settings read. The widget points this at its log,
        /// because "your settings did not load" is precisely the thing that must
        /// never fail silently, and a Debug.WriteLine is not visible in a
        /// release build.
        /// </summary>
        public static Action<string, Exception> OnLoadFailed = null;

        /// <summary>
        /// Parses key=value text over whatever is already in this object, so a
        /// partial file only overrides the keys it mentions. Values that are
        /// out of range or unparseable are ignored rather than thrown, which is
        /// what lets the file be hand-edited.
        /// </summary>
        public void LoadFrom(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException("reader");

            string raw;
            try
            {
                raw = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings read failed, using defaults: " + ex);
                return;
            }

            foreach (string line0 in raw.Split('\n'))
            {
                string line = line0.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                int n;
                switch (k.ToLowerInvariant())
                {
                    case "intervalseconds":
                        if (int.TryParse(v, out n) && n >= 1) IntervalSeconds = n;
                        break;
                    case "resumedelayseconds":
                        if (int.TryParse(v, out n) && n >= 0) ResumeDelaySeconds = n;
                        break;
                    case "movethresholdpixels":
                        if (int.TryParse(v, out n) && n >= 1) MoveThresholdPixels = n;
                        break;
                    case "resetonkeypress":
                        ResetOnKeyPress = ParseBool(v, ResetOnKeyPress);
                        break;
                    case "resetonclick":
                        ResetOnClick = ParseBool(v, ResetOnClick);
                        break;
                    case "resetonscroll":
                        ResetOnScroll = ParseBool(v, ResetOnScroll);
                        break;
                    case "resetonmovement":
                        ResetOnMovement = ParseBool(v, ResetOnMovement);
                        break;
                        case "ignoreinjected":
                            IgnoreInjected = ParseBool(v, IgnoreInjected);
                            break;
                        case "alwaysontop":
                            AlwaysOnTop = ParseBool(v, AlwaysOnTop);
                            break;
                    case "left":
                        if (int.TryParse(v, out n)) { Left = n; SeenLeft = true; }
                        break;
                    case "top":
                        if (int.TryParse(v, out n)) { Top = n; SeenTop = true; }
                        break;
                }
            }
        }

        /// <summary>
        /// Lenient boolean parse. An unrecognised value keeps the default rather
        /// than throwing, so a hand-edited or corrupted file degrades to
        /// working behaviour instead of a widget that will not start.
        /// </summary>
        static bool ParseBool(string v, bool fallback)
        {
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1"
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0"
                || v.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        static string Bool(bool b) { return b ? "true" : "false"; }

        /// <summary>
        /// Renders the current values. Public so the tests can round-trip
        /// without touching the real settings file, which matters because
        /// writing the real file during a test would clobber the user's.
        /// </summary>
        public string Serialize()
        {
            return
                "# TabCycler settings" + Environment.NewLine +
                "IntervalSeconds=" + IntervalSeconds + Environment.NewLine +
                "ResumeDelaySeconds=" + ResumeDelaySeconds + Environment.NewLine +
                "MoveThresholdPixels=" + MoveThresholdPixels + Environment.NewLine +
                "ResetOnKeyPress=" + Bool(ResetOnKeyPress) + Environment.NewLine +
                "ResetOnClick=" + Bool(ResetOnClick) + Environment.NewLine +
                "ResetOnScroll=" + Bool(ResetOnScroll) + Environment.NewLine +
                "ResetOnMovement=" + Bool(ResetOnMovement) + Environment.NewLine +
                "IgnoreInjected=" + Bool(IgnoreInjected) + Environment.NewLine +
                "AlwaysOnTop=" + Bool(AlwaysOnTop) + Environment.NewLine +
                "Left=" + Left + Environment.NewLine +
                "Top=" + Top + Environment.NewLine;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                SaveTo(FilePath);
            }
            catch (Exception ex)
            {
                // Logged, never swallowed: a failed save means the user's
                // settings are not on disk, which is exactly the kind of thing
                // that must not pass silently.
                Debug.WriteLine("TabCycler: settings write failed: " + ex);
                if (OnLoadFailed != null) OnLoadFailed(FilePath, ex);
            }
        }

        /// <summary>
        /// Writes to an arbitrary path, atomically. Separate from <see cref="Save"/>
        /// so the atomic-swap behaviour can be tested against a temporary file
        /// instead of the settings the widget is actually using.
        /// </summary>
        public void SaveTo(string path)
        {
            // Atomic on purpose. Writing the file in place can be caught
            // half-written by a crash or a force-kill, and because the parse
            // keeps the compiled-in default for any key it cannot find, a
            // truncated file does not fail loudly, it quietly reverts every
            // setting it lost to 5s/60s and then bakes that in on the next
            // save. That is how a hand-set 15s became 60s more than once on
            // this machine. Writing a sibling and swapping it in means the real
            // file is either the old contents or the new ones, never a prefix
            // of either.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize());
            if (File.Exists(path))
                File.Replace(tmp, path, null);   // atomic swap
            else
                File.Move(tmp, path);
        }
    }
}
