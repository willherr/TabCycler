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
        public int Left, Top;
        public bool SeenLeft, SeenTop;

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
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
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
                        case "left":
                            if (int.TryParse(v, out n)) { Left = n; SeenLeft = true; }
                            break;
                        case "top":
                            if (int.TryParse(v, out n)) { Top = n; SeenTop = true; }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings read failed, using defaults: " + ex);
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath,
                    "# TabCycler settings" + Environment.NewLine +
                    "IntervalSeconds=" + IntervalSeconds + Environment.NewLine +
                    "ResumeDelaySeconds=" + ResumeDelaySeconds + Environment.NewLine +
                    "Left=" + Left + Environment.NewLine +
                    "Top=" + Top + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TabCycler: settings write failed: " + ex);
            }
        }
    }
}
