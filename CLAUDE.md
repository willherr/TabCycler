# TabCycler

**Visibility: PUBLIC.** Flipped on 2026-09-27 after the safety pass in the
commit history: no credentials, no personal paths, and commit authorship
normalised to `me@will-i-am.dev`.

Floating widget that cycles Windows Terminal tabs on an interval so you can
watch multiple agents work without clicking.

Because this is public now, two things are no longer private thinking:

- Never put a credential, token or key in a commit, an issue or a PR here. The
  repo is indexed and scraped within days of anything that looks like one.
- Commit author metadata is public. Use `me@will-i-am.dev`, not an institutional
  address, per the standing rule about owning the domain long-term.

## Commands

```powershell
# Build the exe
pwsh -NoProfile -File .\src\TabCycler\build.ps1

# Run the tests (52 of them, no desktop needed)
pwsh -NoProfile -File .\src\TabCycler.Tests\run-tests.ps1
pwsh -NoProfile -File .\src\TabCycler.Tests\run-tests.ps1 -Filter Pause
```

Both use the .NET Framework compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). There is no .NET SDK
on this machine and no package restore; do not introduce a dependency that needs
one without checking that first.

## A green build does not mean the P/Invoke works

If you rename a `DllImport` method you **must** set `EntryPoint` to the real
export name. The managed name is otherwise used as the export name, the code
compiles cleanly, CI is green, every logic test passes, and the app throws
`EntryPointNotFoundException` the first time it runs. That happened here, and it
crashed the widget 250ms after launch.

`PlatformTests` exists for exactly this: it calls the real read-only Win32
methods so the failure shows up in the suite. If you add an extern, add a
corresponding call in `PlatformTests`. Never call `InjectNextTab` from a test,
it would send real keystrokes.

## Verifying the widget is actually alive

Do not treat "the process exists" as success, and do not treat a log line as
proof. The crash above happened *after* the `started:` line was written, and the
process stayed alive holding the .NET crash dialog, so both checks passed while
the app was dead.

What actually proves it:

- Process alive and `Responding` well past the first tick (250ms)
- A focus transition in the log, e.g. `terminal focused -> holding 60s` or
  `left Windows Terminal -> Idle`. Either one can only be written if
  `GetForegroundWindow` and `IsTerminalWindow` both resolved and returned live
  values, which is the path that was crashing
- A screenshot, since layout regressions are invisible to every other check

## Do not trust screen coordinates from this shell

This shell runs `SYSTEM_DPI_AWARE`, and the widget is `PER_MONITOR_DPI_AWARE`.
`GetWindowRect` on the widget therefore returns coordinates in a different space
from the one `CopyFromScreen` captures, so cropping a region by those
coordinates lands somewhere else entirely. This cost a lot of confusion: crops
"showed" the widget as clipped when it was simply not there, and the widget was
on the second display.

- Take a full-screen shot and look, rather than cropping blind
- `PrimaryScreen` only covers the primary monitor. There is a second display at
  X=-1, Y=1080 (1280x720). A full-primary screenshot will not show the widget if
  it is on the other one
- `GetDpiForWindow` returns 96 when called from a thread that is not per-monitor
  aware, so do not measure the widget's DPI from this shell. The widget reads
  its own and logs it

## DPI scaling is applied from the handle, not the constructor

`ApplyDpi()` runs in `OnHandleCreated` and uses `GetDpiForWindow`. Do not move
that back into the constructor and do not re-enable `AutoScaleMode.Dpi`:
`CreateGraphics()` reports 96 on this machine regardless of the real display
scale, so WinForms' own autoscaling silently declines to scale and the box ends
up 96-DPI-sized while the fonts render large, which squashes the layout.

## Architecture, and the rule that matters

`CyclerEngine.cs` holds the entire state machine. It has no WinForms, no
P/Invoke, and no clock of its own: the caller passes the current time in and the
engine reports what should happen rather than doing it. Everything platform
specific sits behind `IPlatform`.

**Keep it that way.** The logic used to live inside a `Form` subclass, which
made the only way to check it a live window and a human watching, and four bugs
shipped that way (a bool alongside the state enum, a Pause that did not set its
timer, a click read as user input, a label that disagreed with its own click
handler). If a change needs real input, real focus or a real window to verify,
that is a signal the logic is in the wrong place, not that the test is missing.

The engine has exactly three states, and "stopped" deliberately has one
spelling: pausing, going idle after a return, and reacting to input all land in
`Holding`. Do not add a second way to be stopped.

## Settings and logs

- `%LOCALAPPDATA%\TabCycler\settings.txt`: interval, resume delay, window
  position. Read at launch, so timing changes need no rebuild.
- `%LOCALAPPDATA%\TabCycler\tabcycler.log`: timestamped state transitions,
  rotated at 1 MB. This is the first place to look when behaviour is odd.

## Verification scripts

`verification/*.ps1` are live-desktop checks carried over from development.
They are kept because they were useful, but they are **not** part of CI and
several of them produced misleading results during development. Do not treat a
pass from one of them as evidence, and do not add to them expecting them to be
dependable. The unit tests are the source of truth.
