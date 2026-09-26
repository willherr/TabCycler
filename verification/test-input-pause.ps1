# Verifies the input-driven behaviour: the widget must hold off cycling after
# the user types or clicks, and every new input must push the hold out again.
# Mouse motion is the cheapest input to synthesise, and it goes through the
# same GetLastInputInfo path a real keypress does.
param(
    [int]$ObserveSeconds = 26,
    [int]$Nudges = 3,
    [int]$NudgeGapSeconds = 4
)

Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Fg {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint msg; public ushort l,h; }
  [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
  public static uint Nudge(int dx,int dy){
    INPUT i = new INPUT(); i.type=0; i.u.mo.dx=dx; i.u.mo.dy=dy; i.u.mo.dwFlags=0x0001;
    return SendInput(1, new INPUT[]{i}, Marshal.SizeOf(typeof(INPUT)));
  }
  public static string Name() {
    IntPtr h = GetForegroundWindow();
    if (h == IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h, out pid);
    try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
  }
}
'@

$log = "$env:LOCALAPPDATA\TabCycler\tabcycler.log"
function Cycles { @(Get-Content -LiteralPath $log | Where-Object { $_ -like '*cycle -> sent*' }).Count }
function Nudge {
    # SetCursorPos does NOT move the last-input timestamp, so it cannot be used
    # to test this. A relative SendInput move is real input.
    [void][Fg]::Nudge(3, 0)
    Start-Sleep -Milliseconds 60
    [void][Fg]::Nudge(-3, 0)
}

$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Seconds 1
Write-Output ('foreground: ' + [Fg]::Name())

Write-Output ''
Write-Output 'Phase 1: leave it completely alone, expect cycling to start and continue'
$base = Cycles
Start-Sleep -Seconds $ObserveSeconds
$free = Cycles - $base
Write-Output ('  cycles while untouched: ' + $free + '  (expect ~' + [int]($ObserveSeconds / 5) + ')')

Write-Output ''
Write-Output 'Phase 2: nudge the mouse repeatedly, each nudge must re-arm the hold'
foreach ($i in 1..$Nudges) {
    $before = Cycles
    Nudge
    Start-Sleep -Seconds 1
    $after = Cycles
    Write-Output ('  nudge ' + $i + ': cycles since previous nudge = ' + ($after - $before) + '  (expect 0)')
    Start-Sleep -Seconds $NudgeGapSeconds
}

Write-Output ''
Write-Output '--- log tail ---'
Get-Content -LiteralPath $log | Select-Object -Last 14
