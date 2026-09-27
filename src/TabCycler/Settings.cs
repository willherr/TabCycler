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

        internal static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TabCycler");
            }
        }

        static string FilePath
        {
            get { return Path.Combine(Dir, "settings.txt"); }
        }

        public Settings()
        {
            LoadFromDisk();
        }

        Settings(bool loadFromDisk)
        {
            if (loadFromDisk) LoadFromDisk();
        }

        /// <summary>
        /// A Settings holding only the compiled-in defaults, with no file read.
        /// The public constructor reads the real settings.txt, so a test using
        /// it would inherit whatever the widget last saved and would be testing
        /// the machine rather than the code. This is the hermetic way in.
        /// </summary>
        public static Settings Defaults()
        {
            return new Settings(false);
        }

        /// <summary>
        /// Reads the real settings file, falling back to defaults on any
        /// problem. A corrupt or unreadable file must degrade to working
        /// behaviour, never to a widget that will not start.
        /// </summary>
        void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                using (StreamReader r = new StreamReader(File.ReadAllText(FilePath)))
                    LoadFrom(r);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings read failed, using defaults: " + ex);
            }
        }

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
                "Left=" + Left + Environment.NewLine +
                "Top=" + Top + Environment.NewLine;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, Serialize());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings write failed: " + ex);
            }
        }
    }
}
