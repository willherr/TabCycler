using System;
using System.Runtime.InteropServices;

namespace TabCycler.Tests
{
    /// <summary>
    /// Calls the real Win32 layer. These are the only tests that touch the
    /// actual platform, and they exist because of a specific lesson: a bad
    /// P/Invoke compiles cleanly and then throws EntryPointNotFound on the
    /// first call. Renaming a DllImport without setting EntryPoint passed CI,
    /// passed every logic test, and then crashed the widget 250ms after launch.
    ///
    /// The read-only calls are safe anywhere on Windows, including a headless
    /// CI runner, where GetForegroundWindow simply returns zero. InjectNextTab
    /// is deliberately NOT called: it would send real keystrokes to whatever
    /// the runner happens to have focused.
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
        }

        [Test("IsTerminalWindow rejects a zero handle without throwing")]
        public void IsTerminalWindowRejectsZero()
        {
            Win32Platform p = new Win32Platform();
            Assert.False(p.IsTerminalWindow(IntPtr.Zero), "a zero handle is never the terminal");
        }

        [Test("IsTerminalWindow does not throw on a real handle")]
        public void IsTerminalWindowSurvivesRealHandle()
        {
            Win32Platform p = new Win32Platform();
            // Whatever is in front, this must return rather than throw, even if
            // the owning process has already exited.
            bool result = p.IsTerminalWindow(p.GetForegroundWindow());
            Assert.True(result || !result, "any value is fine; it must not throw");
        }

        [Test("LastInputStamp resolves")]
        public void LastInputStampResolves()
        {
            Win32Platform p = new Win32Platform();
            uint stamp = p.LastInputStamp();
            // On a machine that has been up a while this is a large tick count.
            // Zero is what a failed call returns, so treat it as acceptable but
            // do assert the call completed.
            Assert.True(stamp >= 0, "the call completed without throwing");
        }

        [Test("CursorPosition resolves")]
        public void CursorPositionResolves()
        {
            Win32Platform p = new Win32Platform();
            CursorPos pos = p.CursorPosition();
            Assert.True(pos.X <= int.MaxValue, "the call completed without throwing");
        }

        [Test("the platform satisfies the interface the engine depends on")]
        public void PlatformImplementsInterface()
        {
            Assert.True(new Win32Platform() is IPlatform,
                "the engine is only ever handed the interface, so this must hold");
        }
    }
}
