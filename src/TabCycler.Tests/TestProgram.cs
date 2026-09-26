using System;

namespace TabCycler.Tests
{
    internal static class TestProgram
    {
        private static int Main(string[] args)
        {
            string filter = args != null && args.Length > 0 ? args[0] : null;
            if (!string.IsNullOrEmpty(filter))
                Console.WriteLine("Running tests matching: " + filter + Environment.NewLine);
            return TestRunner.Run(filter);
        }
    }
}
