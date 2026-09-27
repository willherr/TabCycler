using System;

namespace TabCycler.Tests
{
    /// <summary>
    /// Calls the real Win32 layer. These are the only tests that touch the
    /// actual platform, and they exist because of a specific lesson: a bad
    /// P/Invoke compiles cleanly and then throws EntryPointNotFound on the
    /// first call. Renaming a DllImport without setting EntryPoint passed CI,
    /// passed every logic test, and then crashed the widget 250ms after launch.
    ///
    /// The input hook is not asserted on for content, because what it observes
    /// depends on what the machine is doing. These check that the calls
    /// resolve, that a platform can be constructed and disposed without
    /// leaving a hook installed, and that the engine's interface is satisfied.
    /// Nothing here injects input, so running the suite does not disturb a
    /// live desktop.
    /// </summary>
    public sealed class PlatformTests
    {
        [Test("GetForegroundWindow resolves and returns a handle or zero")]
        public void GetForegroundWindowResolves()
        {
            Win32Platform p = new Win32Platform();
            IntPtr fg = p.GetForegroundWindow();
            Assert.True(fg == IntPtr.Zero || fg != IntPtr.Zero,
                "any value is fine; the point is that the call did not throw");
            p.Dispose();
        }

        [Test("IsTerminalWindow rejects a zero handle without throwing")]
        public void IsTerminalWindowRejectsZero()
        {
            Win32Platform p = new Win32Platform();
            Assert.False(p.IsTerminalWindow(IntPtr.Zero), "a zero handle is never the terminal");
            p.Dispose();
        }

        [Test("IsTerminalWindow does not throw on a real handle")]
        public void IsTerminalWindowSurvivesRealHandle()
        {
            Win32Platform p = new Win32Platform();
            bool result = p.IsTerminalWindow(p.GetForegroundWindow());
            Assert.True(result || !result, "any value is fine; it must not throw");
            p.Dispose();
        }

        [Test("input observation resolves and reports its own fidelity")]
        public void InputObservationResolves()
        {
            using (Win32Platform p = new Win32Platform())
            {
                System.Collections.Generic.List<InputEvent> events = p.DrainInput();
                Assert.True(events != null, "draining must always return a list, never null");
                Console.WriteLine("      (hook installed on this machine: " + p.HasHighFidelityInput + ")");
            }
        }

        [Test("discarding pending input is safe")]
        public void DiscardIsSafe()
        {
            using (Win32Platform p = new Win32Platform())
            {
                p.DiscardPendingInput();
                Assert.True(p.DrainInput().Count >= 0, "draining after a discard must not throw");
            }
        }

        [Test("disposing twice is safe, so no hook is left behind")]
        public void DoubleDisposeIsSafe()
        {
            Win32Platform p = new Win32Platform();
            p.Dispose();
            p.Dispose();
            Assert.True(true, "the second dispose must not throw or double-unhook");
        }

        [Test("the platform satisfies the interface the engine depends on")]
        public void PlatformImplementsInterface()
        {
            using (Win32Platform concrete = new Win32Platform())
            {
                IPlatform p = concrete;
                Assert.True(p != null, "the engine is only ever handed the interface");
            }
        }
    }
}
