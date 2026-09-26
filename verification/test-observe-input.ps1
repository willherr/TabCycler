# Samples GetLastInputInfo and the cursor position on the same 250ms cadence the
# widget polls at, so the log can be lined up against it. Answers: is the input
# stamp really changing, and is the cursor moving with it?
param([int]$Samples = 24)

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class Obs {
  [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(out LASTINPUTINFO p);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  public static uint Stamp(){ LASTINPUTINFO l=new LASTINPUTINFO(); l.cbSize=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO)); GetLastInputInfo(out l); return l.dwTime; }
  public static string Cur(){ POINT p; GetCursorPos(out p); return p.X+","+p.Y; }
  public static string Fg(){
    IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h,out pid);
    string n="?";
    try { n=System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
    var sb=new StringBuilder(120); GetWindowTextW(h,sb,120);
    return n+" ["+sb.ToString()+"]";
  }
}
'@

$log = "$env:LOCALAPPDATA\TabCycler\tabcycler.log"
$prevStamp = [Obs]::Stamp()
$prevCur = [Obs]::Cur()

for ($i = 1; $i -le $Samples; $i++) {
    Start-Sleep -Milliseconds 250
    $s = [Obs]::Stamp()
    $c = [Obs]::Cur()
    $moved = ($c -ne $prevCur)
    $changed = ($s -ne $prevStamp)
    $mark = if ($changed -and $moved) { 'input+move  ' }
            elseif ($changed)          { 'INPUT, no move' }
            else                      { 'no input    ' }
    Write-Output ('{0,3}  stamp+{1,-5} cursor {2,-12} fg {3,-42} {4}' -f $i, ($s - $prevStamp), $c, [Obs]::Fg(), $mark)
    $prevStamp = $s
    $prevCur = $c
}

Write-Output ''
Write-Output '--- widget log, same window ---'
Get-Content -LiteralPath $log | Select-Object -Last 14
