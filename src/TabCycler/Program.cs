using System;
using System.Threading;
using System.Windows.Forms;

namespace TabCycler
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // One instance only. Two of them fight over the same settings file
            // and the same position, and the loser is decided by who closes
            // last: an instance holding stale timings writes them over whatever
            // the newer one saved, so a setting the user had just changed
            // silently reverted on the next launch. A named mutex is the cheapest
            // way to refuse the second launch.
            //
            // Silently, with no dialog. A "already running" message box would
            // take focus off the terminal, which is the one thing this widget is
            // built never to do, and it would do it on an accidental
            // double-launch. The running widget is already on screen, which is
            // what the second launch was asking for.
            bool first;
            using (Mutex owned = new Mutex(true, @"Local\TabCycler.SingleInstance", out first))
            {
                if (!first) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // The platform owns the low-level keyboard and mouse hooks, so it
                // has to be disposed rather than left to process teardown.
                using (var platform = new Win32Platform())
                {
                    Application.Run(new CyclerForm(platform));
                }
            }
        }
    }
}
