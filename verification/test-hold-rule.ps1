# Verifies the two halves of the hold rule after the mouse-movement fix:
#   pointer movement  -> must NOT re-arm the hold (this is what made the
#                       countdown sit at 60 forever)
#   key press / click -> MUST re-arm the hold
param(
    [int]$ResumeDelaySeconds = 6,
    [int]$MoveOnlySeconds = 12,
    [int]$KeyOnlySeconds = 12
)

Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class Inp {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint msg; public ushort l,h; }
  [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  static INPUT Mouse(int dx,int dy){ INPUT i=new INPUT(); i.type=0; i.u.mo.dx=dx; i.u.mo.dy=dy; i.u.mo.dwFlags=0x0001; return i; }
  static INPUT Key(ushort vk,uint f){ INPUT i=new INPUT(); i.type=1; i.u.ki.vk=vk; i.u.ki.dwFlags=f; return i; }
  // Real pointer movement: a NET displacement each step, exactly what a
  // drifting hand does. An out-and-back that returns to the same pixel reads
  // as "the pointer did not move" at poll time and is not a valid test.
  public static void Drift(int n){
    for(int i=0;i<n;i++){
      SendInput(1,new INPUT[]{Mouse(2,1)},Marshal.SizeOf(typeof(INPUT)));
      Thread.Sleep(700);
    }
  }
  public static void TapKey(){ SendInput(2,new INPUT[]{Key(0x7A,0),Key(0x7A,2)},Marshal.SizeOf(typeof(INPUT))); }
  public static string Cursor(){ POINT p; GetCursorPos(out p); return p.X + "," + p.Y; }
  public static string FgName(){
    IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h,out pid);
    try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
  }
}
'@

$root = 'C:\Users\wch\Tools\TabCycler'
$log  = "$env:LOCALAPPDATA\TabCycler\tabcycler.log"
$dir  = Split-Path $log

# Restart the widget on a short hold so the transitions are observable.
Get-Process -Name TabCycler -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
@('# TabCycler settings','IntervalSeconds=5',"ResumeDelaySeconds=$ResumeDelaySeconds",'Left=1416','Top=72') |
    Set-Content -LiteralPath "$dir\settings.txt"
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Start-Process -FilePath "$root\TabCycler.exe"
Start-Sleep -Seconds 2

$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Seconds 1
Write-Output ('foreground: ' + [Inp]::FgName())

function Arm { @(Get-Content -LiteralPath $log | Where-Object { $_ -like '*input -> holding*' }).Count }
function Cycling { @(Get-Content -LiteralPath $log | Where-Object { $_ -like '*hold elapsed*' }).Count }

Write-Output ''
Write-Output "TEST A: hold runs out and cycling starts, then the mouse drifts for ${MoveOnlySeconds}s"
Start-Sleep -Seconds ($ResumeDelaySeconds + 2)
$armA = Arm
Write-Output ('  cursor before drift: ' + [Inp]::Cursor())
[Inp]::Drift([int]($MoveOnlySeconds / 0.7)) | Out-Null
Write-Output ('  cursor after drift:  ' + [Inp]::Cursor())
Start-Sleep -Seconds 2
Write-Output ('  holds armed by the drift: ' + ((Arm) - $armA) + '   (expect 0)')
Write-Output ('  times cycling started:     ' + (Cycling) + '   (expect >= 1)')

Write-Output ''
Write-Output "TEST B: pointer held still, press one key -> must arm the hold"
$armB = Arm
[Inp]::TapKey()
Start-Sleep -Seconds 2
Write-Output ('  holds armed by the key:    ' + ((Arm) - $armB) + '   (expect 1)')
Start-Sleep -Seconds ($KeyOnlySeconds + 1)
Write-Output ('  cycling restarted after hold: ' + (Cycling) + ' total   (expect >= 2)')

Write-Output ''
Write-Output '--- log ---'
Get-Content -LiteralPath $log
