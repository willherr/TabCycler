# Regression test for: pressing Start immediately bounced back to "Holding".
# Sends a genuine left click at the Start button (the same thing a user does)
# and asserts the hold is NOT re-armed afterwards.
param([int]$ObserveSeconds = 16)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName Microsoft.VisualBasic

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class Ui {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint msg; public ushort l,h; }
  [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
  public static uint Click(int x,int y){
    SetCursorPos(x,y); Thread.Sleep(150);
    INPUT[] b = new INPUT[2];
    b[0]=new INPUT(); b[0].type=0; b[0].u.mo.dwFlags=0x0002;
    b[1]=new INPUT(); b[1].type=0; b[1].u.mo.dwFlags=0x0004;
    return SendInput(2,b,Marshal.SizeOf(typeof(INPUT)));
  }
  public static string Fg(){
    IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h,out pid);
    try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
  }
}
'@

$log = "$env:LOCALAPPDATA\TabCycler\tabcycler.log"
function Count($pat) { @(Get-Content -LiteralPath $log | Where-Object { $_ -like "*$pat*" }).Count }

$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Seconds 2

$p = Get-Process -Name TabCycler | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'Start')
$btn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $btn) {
    $names = ($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
               [System.Windows.Automation.Condition]::TrueCondition) |
              ForEach-Object { $_.Current.Name }) -join ', '
    Write-Output ("no Start button present; widget shows: " + $names)
    exit 1
}
$r = $btn.Current.BoundingRectangle
$x = [int]($r.X + $r.Width/2); $y = [int]($r.Y + $r.Height/2)
Write-Output ('foreground before click : ' + [Ui]::Fg())
Write-Output ('clicking Start at       : ' + $x + ',' + $y)

$armsBefore = Count 'input -> holding'
$sent = [Ui]::Click($x, $y)
Start-Sleep -Milliseconds 400
Write-Output ('click delivered         : ' + $sent + ' of 2 events')
Write-Output ('foreground after click  : ' + [Ui]::Fg() + '   (must stay WindowsTerminal)')

Start-Sleep -Seconds $ObserveSeconds
$armsAfter = Count 'input -> holding'
$cycles = Count 'cycle -> sent'

Write-Output ''
Write-Output ('holds re-armed after Start : ' + ($armsAfter - $armsBefore) + '   (expect 0)')
Write-Output ('cycles after Start         : ' + $cycles + '   (expect >= ' + [int]($ObserveSeconds/5) + ')')
Write-Output ''
Write-Output '--- log ---'
Get-Content -LiteralPath $log
