# TabCycler

**Visibility: PRIVATE** while it is being vetted. Tracked in the "flip to
public" issue; do not make this public until that issue's checklist is done.

Floating widget that cycles Windows Terminal tabs on an interval so you can
watch multiple agents work without clicking.

## Commands

```powershell
# Build the exe
pwsh -NoProfile -File .\src\TabCycler\build.ps1

# Run the tests (33 of them, no desktop needed)
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
