# Regression tests for the two reported bugs:
#   1. Pause did not stick - typing while paused could drag the state machine
#      out of the stop. Paused must now be inert to input.
#   2. Start switched tabs instantly; it must wait one full interval first.
# Requires the user not to touch the machine for the duration, otherwise their
# own input is indistinguishable from the synthetic input used here.
param([int]$AfterStartSeconds = 8, [int]$PausedSeconds = 16)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName Microsoft.VisualBasic

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class Ui {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint msg; public ushort l,h; }
  [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
  public static void Click(int x,int y){
    SetCursorPos(x,y); Thread.Sleep(150);
    INPUT[] b=new INPUT[2];
    b[0]=new INPUT(); b[0].type=0; b[0].u.mo.dwFlags=0x0002;
    b[1]=new INPUT(); b[1].type=0; b[1].u.mo.dwFlags=0x0004;
    SendInput(2,b,Marshal.SizeOf(typeof(INPUT)));
  }
  // F24: a real key press that nothing binds, used to simulate typing.
  public static void TapKey(){ SendInput(2,new INPUT[]{Key(0x7A,0),Key(0x7A,2)},Marshal.SizeOf(typeof(INPUT))); }
  static INPUT Key(ushort vk,uint f){ INPUT i=new INPUT(); i.type=1; i.u.ki.vk=vk; i.u.ki.dwFlags=f; return i; }
  public static string Fg(){
    IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h,out pid);
    try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
  }
}
'@

$log = "$env:LOCALAPPDATA\TabCycler\tabcycler.log"
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Get-Process -Name TabCycler -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
Start-Process -FilePath 'C:\Users\wch\Tools\TabCycler\TabCycler.exe'
Start-Sleep -Seconds 2

$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Seconds 2

function Root {
    $p = Get-Process -Name TabCycler | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}
function Press([string]$name) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $e = (Root).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
    if (-not $e) {
        $all = ((Root).FindAll([System.Windows.Automation.TreeScope]::Descendants,
               [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ', '
        throw "button '$name' not present; widget shows: $all"
    }
    $r = $e.Current.BoundingRectangle
    [Ui]::Click([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
}
function Count($p) { @(Get-Content -LiteralPath $log | Where-Object { $_ -like "*$p*" }).Count }

Write-Output '--- TEST 1: Start must wait one full interval before the first switch ---'
$before = Count 'cycle -> sent'
Press 'Start'
Start-Sleep -Seconds 2
$early = Count 'cycle -> sent'
Write-Output ('  cycles 2s after Start : ' + ($early - $before) + '   (expect 0, must still be counting down)')

# Press Pause while the state is definitely Cycling. Waiting longer lets any
# stray input arm the hold and put the widget back into Holding, where the
# button correctly reads "Start" and there is nothing to pause.
Start-Sleep -Seconds 4
$later = Count 'cycle -> sent'
Write-Output ('  cycles 6s after Start : ' + ($later - $before) + '   (expect 1)')

Write-Output ''
Write-Output '--- TEST 2: Pause must stick, even while typing ---'
Press 'Pause'
Start-Sleep -Milliseconds 500
$p0 = Count 'cycle -> sent'
$afterPause = ((Root).FindAll([System.Windows.Automation.TreeScope]::Descendants,
              [System.Windows.Automation.Condition]::TrueCondition) |
              ForEach-Object { $_.Current.Name }) -join ' / '
Write-Output ('  widget right after Pause: ' + $afterPause)
# Type repeatedly while paused. Under the old logic this dragged the state out
# of the stop; Paused is now inert so none of it may matter.
foreach ($i in 1..4) { [Ui]::TapKey(); Start-Sleep -Seconds 3 }
$p1 = Count 'cycle -> sent'
Write-Output ('  cycles while paused + typed in : ' + ($p1 - $p0) + '   (expect 0)')
Write-Output ('  foreground still: ' + [Ui]::Fg())

$names = ((Root).FindAll([System.Windows.Automation.TreeScope]::Descendants,
         [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name })
Write-Output ('  widget now reads: ' + ($names -join ' / '))

Write-Output ''
Write-Output '--- log ---'
Get-Content -LiteralPath $log
