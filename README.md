# TabCycler

A small always-on-top widget that rotates the active Windows Terminal tab on an
interval, so you can watch several agents work without clicking through tabs.

<!-- Status: private while it is being vetted. See the open issues. -->

## What it does

- Switches to the next tab every 5 seconds while Windows Terminal is in front
- The moment you type or click, it stops and holds for 60 seconds
- Every further keystroke or click pushes that 60 seconds out again
- Moving the mouse does not count, so you can still gesture at things
- A **Start** button skips the wait, and waits a full interval before the first
  switch so the tab you just chose stays readable
- **X** quits

The button is contextual: it reads **Pause** while cycling and **Start** in any
other state, and its label is derived from the same value the click handler
reads, so it can never promise something the press does not do.

## Build

Needs nothing but Windows. Both steps use the .NET Framework compiler that
ships with the OS, so there is no SDK, no runtime install and no package
restore.

```powershell
pwsh -NoProfile -File .\src\TabCycler\build.ps1
```

Produces `src/TabCycler/TabCycler.exe`. Launch it, or pin the running taskbar
button.

## Test

```powershell
pwsh -NoProfile -File .\src\TabCycler.Tests\run-tests.ps1
pwsh -NoProfile -File .\src\TabCycler.Tests\run-tests.ps1 -Filter Pause
```

33 tests, no external test package, and no desktop required. The state machine
is a plain class that takes the current time as a parameter and reports what
should happen rather than doing it, so the tests drive it with a fake platform
and a synthetic clock. That is deliberate: the logic originally lived inside a
`Form` subclass, which meant the only way to check it was to poke a live window,
and four separate bugs got through that way.

`PlatformTests` calls the real Win32 layer, read-only. Those exist because a
wrong `DllImport` compiles perfectly happily and then throws
`EntryPointNotFoundException` on the first call, so a green build says nothing
about whether the entry points exist.

## How it works

| File | Role |
| --- | --- |
| `src/TabCycler/CyclerEngine.cs` | The entire state machine. No WinForms, no P/Invoke, no clock. |
| `src/TabCycler/IPlatform.cs` | The five things the engine needs from the outside world. |
| `src/TabCycler/Win32Platform.cs` | The real implementation: `GetLastInputInfo`, `GetCursorPos`, `SendInput`. |
| `src/TabCycler/CyclerForm.cs` | Paint and click handling only. |
| `verification/*.ps1` | Live-desktop checks kept from development. Not part of CI, and not reliable enough to be. |

Tab switching is a synthetic `Ctrl+Tab`, which Windows Terminal treats as its
own next-tab binding. The whole batch is sent in one `SendInput` call so no
window can observe a bare Tab that a shell would read as an indent.

**It will keep your machine looking awake.** The injected keystroke resets
Windows' global last-input timestamp, which is what Teams and other presence
tools read to decide you are idle. Measured here: idle time went from 67,975 ms
to 0 ms on injection. So while it is cycling, you will not flip to Away. See the
open issue about switching to `wt focus-tab` to avoid that.

## Settings

`%LOCALAPPDATA%\TabCycler\settings.txt`, written on exit:

```ini
IntervalSeconds=5
ResumeDelaySeconds=60
Left=1416
Top=72
```

Timings are read at launch, so changing them needs no rebuild.

## Limitations

- Windows Terminal only. It identifies the terminal by process name, and relies
  on its `ctrl+tab` binding.
- A tab running a raw-mode program (vim, less, a full-screen TUI) may swallow
  the keystroke.
- A tab in that state may also make pointer movement land oddly. The movement
  exclusion keys off whether the pointer actually changed position between
  polls, so a raw-mode terminal that captures the mouse could defeat it.
- The keystroke goes to whatever has focus at that instant. Focus is
  re-checked immediately before each send, so the window is microseconds wide,
  but it is not zero.
