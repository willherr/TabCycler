using System;
using System.Windows.Forms;

namespace TabCycler
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // The platform owns the low-level keyboard and mouse hooks, so it has
            // to be disposed rather than left to process teardown.
            using (var platform = new Win32Platform())
            {
                Application.Run(new CyclerForm(platform));
            }
        }
    }
}
