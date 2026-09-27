# Samples the system input stamp and pointer position together to classify
# whatever is generating input on this machine: pointer movement, keys/clicks
# (stamp changes with the pointer still), or nothing at all.
# Touch nothing while this runs.
param([int]$Samples = 60, [int]$IntervalMs = 500)

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(out LASTINPUTINFO p);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  public static uint Stamp(){ LASTINPUTINFO l=new LASTINPUTINFO(); l.cbSize=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO)); GetLastInputInfo(out l); return l.dwTime; }
  public static POINT Cur(){ POINT p; GetCursorPos(out p); return p; }
  public static string Fg(){
    IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h,out pid);
    string n="?"; try { n=System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
    var sb=new StringBuilder(90); GetWindowTextW(h,sb,90);
    return n+" ["+sb.ToString()+"]";
  }
}
'@

$prevStamp = [W]::Stamp()
$prevCur = [W]::Cur()
$events = @()
$moveCount = 0; $keyCount = 0

Write-Output "watching for $([int]($Samples * $IntervalMs / 1000))s, touch nothing"
Write-Output ''

for ($i = 1; $i -le $Samples; $i++) {
    Start-Sleep -Milliseconds $IntervalMs
    $s = [W]::Stamp()
    $c = [W]::Cur()
    if ($s -ne $prevStamp) {
        $dx = [Math]::Abs($c.X - $prevCur.X)
        $dy = [Math]::Abs($c.Y - $prevCur.Y)
        $kind = if ($dx -gt 0 -or $dy -gt 0) { 'POINTER MOVE' } else { 'KEY / CLICK / SCROLL' }
        if ($dx -gt 0 -or $dy -gt 0) { $moveCount++ } else { $keyCount++ }
        $events += [pscustomobject]@{ At = $i; Kind = $kind; Dx = $dx; Dy = $dy; Fg = [W]::Fg() }
    }
    $prevStamp = $s
    $prevCur = $c
}

Write-Output "input events detected: $($events.Count)  (pointer move: $moveCount, key/click/scroll: $keyCount)"
Write-Output ''
if ($events.Count -eq 0) {
    Write-Output "NOTHING generated input during the window. The machine was genuinely idle."
} else {
    $events | Select-Object -First 25 | Format-Table -AutoSize
    if ($events.Count -gt 25) { Write-Output "... and $($events.Count - 25) more" }
}
