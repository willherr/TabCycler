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
            Application.Run(new CyclerForm(new Win32Platform()));
        }
    }
}
