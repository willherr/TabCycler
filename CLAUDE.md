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

# Run the tests (77 of them, no desktop needed)
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

## The two monitors have different scales, and that is the whole bug

Measured, not assumed:

| Display | Position | Size | Primary | Scale |
|---|---|---|---|---|
| `\\.\DISPLAY1` external | -1, 1080 | 1280x720 | no | **150%** (144 DPI) |
| `\\.\DISPLAY2` laptop panel | 0, 0 | 1920x1080 | yes | **100%** (96 DPI) |

- Ask with **`GetScaleFactorForMonitor`** (Shcore.dll), which returns 100 / 125 /
  150 / 175 / 200 directly. `GetDpiForMonitor` is useless here: it is not
  DPI-aware itself and returns **96 for both monitors** to a per-monitor-aware
  caller, which is not evidence about either display. A 96 in that API's output
  is the API, not the machine.
- Never infer a monitor's scale from `GetDpiForWindow` on the widget, or from
  `AppliedDPI` under `HKCU\Control Panel\Desktop`. The external monitor at 150%
  means the system DPI is 96, and the widget at 1440,0 is on the 100% panel, so
  `dpi=96 box=320x82` is the **correct** answer there, not a fault. Chasing that
  96 as a bug sent this in circles for a while.
- The symptom is a **transition**, not a resting state: dragging from the
  external 150% display to the laptop 100% panel left a 320x82 box holding
  150%-scaled fonts, so the buttons jammed against the edge and the hint text
  clipped.

## DPI: WM_DPICHANGED is the only authoritative source

Two independent scalings are in play and they must agree or the layout is wrong:

1. The box and every position are multiplied out by `_scale`, because the
   geometry is authored at 96 DPI.
2. The fonts are created in **raw points**, so WinForms realises them against
   the handle's own DPI when it paints.

So (1) must be computed from the same DPI that (2) will use. That is why the
DPI is passed in rather than re-read at layout time, and the place that owns
the rule is `DpiAwareForm`, not a per-form copy:

- `WM_DPICHANGED` (`LOWORD(wParam)`) is authoritative and is the only thing that
  fires on a move between monitors. It runs `base.WndProc` first so WinForms
  updates its own bookkeeping, then re-lays out.
- `OnShown` catches the initial case, because the first layout happens *before*
  `RestorePosition` has moved the widget to its target monitor, so the DPI read
  at that point can describe a monitor the widget is not on.
- `GetDpiForWindow` is only the first guess at handle creation.

Both `CyclerForm` and `SettingsDialog` derive from `DpiAwareForm` and implement
`OnDpiScaled`. That is deliberate: this trigger was written once for the widget,
got the wrong override, and a copy pasted into the settings dialog would have
carried the same defect. Add a third window by deriving from the base, not by
copying the `WndProc`.

Do not re-introduce `OnDpiChangedAfterParent` for this. That is the child-control
hook (a child's parent changed DPI), so for a top-level `Form` it never fired and
dragging between monitors resized nothing at all. That was the actual defect.

Keep `AutoScaleMode = None` and do not move any of this into the constructor.
`CreateGraphics()` reports 96 on this machine regardless of the real scale, so
WinForms' own autoscaling silently declines to scale.

The widget additionally multiplies everything by `DesignScale` (1.4), because
9pt type is too small to read on a 100% panel where a point is only a pixel. That
is a legibility choice, not a DPI correction, and it is one constant. The
dialog has no such multiplier: it is an ordinary window and 9pt is ordinary
there.

Verify a change by moving the window with `SetWindowPos` across the monitor
boundary and reading the `dpi=` lines. `SetWindowPos` does not touch the mouse
and, because the window is `WS_EX_NOACTIVATE`, does not steal focus either. On a
100% panel the shell's coordinate space matches the widget's, so a crop at that
point is meaningful; on the 150% panel it is not, so check the log there.


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

The engine has four states, and the rule about them is narrower than it looks.
It used to say "never add a fourth state", because a bool alongside the state
enum had shipped once and the two could disagree, so a button press did the
opposite of what its label promised. That reasoning was right and the
conclusion was wrong: folding Pause into `Holding` conflated a **timed wait
that ends by itself** with a **deliberate pause that ends when the user says
so**, and Pause quietly restarted after the resume delay.

So `Paused` exists. What is still forbidden is a second way to say stopped, and
in particular a bool next to this enum, which is how the original disagreement
happened.

- `Paused` survives losing focus and ignores input. A pause that a background
  process can undo is not a pause, and this machine has background processes
  driving the cursor.
- **Start restarts the hold for one cycle interval**, then cycles. Not zero, and
  not the resume delay. Zero means the button gives no sign the press landed,
  and pressing Start twice in a row looks like nothing happened. The resume
  delay is sized for "I walked away", and making someone wait 60s after they
  pressed Start is not what they meant.
- `ShouldBeTopMost` follows `TerminalInFront`, not the state, because a pause
  outlives losing focus: the widget must stop floating over the browser while
  paused but still come back paused. `AlwaysOnTop` is the setting that pins it
  regardless.

## The poll timer is 250ms and nothing else

The widget's countdown is only as smooth as the poll that redraws it. The tick
is a 250ms UI poll, and `IntervalSeconds` is how long the engine waits between
tab switches. Those are different numbers.

They were conflated once: saving settings did
`_tick.Interval = IntervalSeconds * 1000`, so with "cycle every 5s" the entire UI
refreshed every five seconds and the countdown appeared to jump 15, 10, 5. The
remedy is not to retune the poll but to keep the two apart. If you find yourself
writing to `_tick.Interval` anywhere but the constructor, that is the bug.

## One instance only

`Program.Main` takes a named mutex. Two instances overwrite each other's
settings and position, and the winner is decided by who closes last: an
instance holding stale timings writes them over whatever the newer one saved,
so a setting the user had just changed silently reverted on the next launch.
This is not hypothetical, it is how a "wait after you stop" value of 15s went
back to 60s.

The second launch exits **silently**. A "already running" message box would take
focus off the terminal, which is the one thing this widget exists not to do, and
it would do it on an accidental double-launch.

## Input detection, and the limit that cannot be engineered away

`Win32Platform` installs `WH_KEYBOARD_LL` and `WH_MOUSE_LL` and reports typed
events (key, click, scroll, movement) through `DrainInput()`. That replaced
`GetLastInputInfo`, which could only say that something happened, never what.
The old path is still there as the fallback when a hook cannot be installed.

**Do not try to identify the process behind an input event.** The
`LLKHF_INJECTED` / `LLMHF_INJECTED` bit is set by `SendInput` only.
Automation that drives the real cursor through `SetCursorPos` or `mouse_event`
produces events with the bit clear, which are indistinguishable from a human
hand, and Windows exposes no way to attribute such an event to a process. Some
background automation on this machine will always be counted as the user. This
was measured, not guessed, and an hour went into the wrong lead because the
observation that started it was `flags == 0` on every event.

What is worth remembering: the widget's own `SendInput` for Ctrl+Tab is the one
source of synthetic input here that *is* correctly ignored, and it used to reset
its own hold. `IgnoreInjected` in `settings.txt` turns the filtering off, and
each input kind can be armed or disarmed individually.

## Settings and logs

- `%LOCALAPPDATA%\TabCycler\settings.txt`: interval, resume delay, movement
  threshold, the four input toggles, `IgnoreInjected`, `AlwaysOnTop`, and window
  position. Edited from the gear button on the widget, and still hand-editable:
  the parse is lenient and keeps defaults for anything it cannot read. **Use
  `Settings.Defaults()` in tests, never `new Settings()`**, because the public
  constructor reads the real file and a test using it would inherit whatever the
  widget last saved. `LoadFrom(TextReader)` and `Serialize()` are the seam that
  keeps the tests off the disk.
- `AlwaysOnTop` is **off** by default. The widget already floats above other
  windows while the terminal is in front, which is the only time it is useful;
  the setting restores the old unconditional behaviour for anyone who parks the
  terminal behind something else.
- `%LOCALAPPDATA%\TabCycler\tabcycler.log`: timestamped state transitions,
  settings applications and DPI changes, rotated at 1 MB. This is the first
  place to look when behaviour is odd.

## The settings dialog

The gear on the widget opens `SettingsDialog`. It edits a copy and is seeded
from the **engine**, not from the settings file, so it can never open showing
something other than what is actually running. Cancel writes nothing.

Applying is live: `CyclerEngine.ApplySettings` takes the new values with the
same validation as the constructor, and the widget reschedules the tick period
to match. A rejected value is logged and the file is left alone, so the file
and the running state cannot end up disagreeing.

A timing change reschedules the pending wait **from now**, rather than keeping
the old phase. Preserving the phase would mean changing 5s to 60s could fire
*sooner* than the new interval promises, which is the opposite of what was
asked for.

The hint line under the status is built from whatever is actually enabled, so
it cannot claim movement re-arms the hold when movement is switched off. With
everything off it says so rather than showing an empty list.

## Icons are painted, not typed

The gear and the close glyph are drawn in `OnGearPaint` / `OnClosePaint`. A
literal U+2699 in the source is a gamble: `csc` reads these files as UTF-8 only
when they carry a BOM, and without one the glyph arrives mangled, which shows up
on someone else's build and nowhere else.

The close button follows the native pattern: the face matches the chrome and
turns red only under the pointer. The face has to be
`FlatAppearance.MouseOverBackColor`, not `BackColor`, because WinForms uses that
instead while the pointer is over a flat button. Setting `BackColor` from a
`MouseEnter` handler therefore does nothing, and doing both makes the button
change colour twice per hover. An always-red close button was also tried and
rejected: it shouts on a widget that is otherwise quiet.

Both icons are sized as a fraction of the button so they land identically at
100% and 150%.

## Colours

`Palette.cs` holds them, because the dialog has to match the widget and a
second copy of the hex values would drift. Every pair there is contrast-checked,
not eyeballed. The one that was not: the border used to be `#444444`, which is
1.69:1 against the `#1F1F1F` background and therefore fails the 3:1 a non-text
boundary needs. It is `#707070` now, 3.33:1.

## Verification scripts

`verification/*.ps1` are live-desktop checks carried over from development.
They are kept because they were useful, but they are **not** part of CI and
several of them produced misleading results during development. Do not treat a
pass from one of them as evidence, and do not add to them expecting them to be
dependable. The unit tests are the source of truth.

To exercise the real UI without disturbing anything, post window messages
rather than moving the mouse: `BM_CLICK` to a button's handle opens the dialog
and ticks a checkbox, and unlike synthetic input it does not trip the
low-level hook in `Win32Platform`, which would otherwise re-arm the hold and
make the test lie to you.
