# End-to-end TabCycler verification, run as a SINGLE process on purpose.
# Earlier attempts spawned helper pwsh processes per step, and each one stole
# focus from Windows Terminal, which flipped the widget to Idle and made the
# feature look broken when it was not.
#
# Also verifies the WS_EX_NOACTIVATE fix: clicking the widget's button must not
# pull focus away from Windows Terminal.
param([int]$RunSeconds = 12, [int]$PauseSeconds = 13)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName Microsoft.VisualBasic

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Ui {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int sz);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, MOUSEINPUT[] i, int sz);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
  const uint MOVE=1, LEFTDOWN=0x0002, LEFTUP=0x0004;
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    INPUT[] b = new INPUT[2];
    b[0]=Mk(LEFTDOWN); b[1]=Mk(LEFTUP);
    SendInput(2, b, Marshal.SizeOf(typeof(INPUT)));
  }
  static INPUT Mk(uint f) { INPUT i = new INPUT(); i.type=MOVE; i.mi.dwFlags=f; return i; }
  public static string FgName() {
    IntPtr h = GetForegroundWindow();
    if (h == IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h, out pid);
    try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
  }
}
'@

$root = 'C:\Users\wch\Tools\TabCycler'
$log  = "$env:LOCALAPPDATA\TabCycler\tabcycler.log"

function Get-WidgetRoot {
    $p = Get-Process -Name TabCycler -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { throw 'TabCycler is not running' }
    [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}
function Get-ElementByName($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Click-Element($root, [string]$name) {
    $e = Get-ElementByName $root $name
    if (-not $e) {
        $names = ($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                   [System.Windows.Automation.Condition]::TrueCondition) |
                  ForEach-Object { $_.Current.Name }) -join ', '
        throw "'$name' not present. Widget shows: $names"
    }
    $r = $e.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2)
    $y = [int]($r.Y + $r.Height / 2)
    [Ui]::Click($x, $y)
    return ('clicked ' + $name + ' at ' + $x + ',' + $y)
}
function Get-ActiveTab {
    $wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    $r = [System.Windows.Automation.AutomationElement]::FromHandle($wt.MainWindowHandle)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    foreach ($t in $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        try {
            $p = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($p.Current.IsSelected) { return $t.Current.Name }
        } catch {}
    }
    return '(none)'
}
function Cycles { (Select-String -LiteralPath $log -Pattern 'cycle -> sent' -ErrorAction SilentlyContinue).Count }

# ---- focus Windows Terminal, the way the user would ----
$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Seconds 2
Write-Output ('foreground: ' + [Ui]::FgName())
$root = Get-WidgetRoot
Write-Output ('widget offers: ' + ((Get-ElementByName $root 'Start') ? 'Start' : (Get-ElementByName $root 'Pause') ? 'Pause' : '?'))

Write-Output ''
Write-Output '--- press Start (must skip the 60s hold) ---'
Click-Element $root 'Start' | Write-Output
Start-Sleep -Seconds 1
Write-Output ('foreground after click (must stay WindowsTerminal): ' + [Ui]::FgName())
$c0 = Cycles; $t0 = Get-ActiveTab
Start-Sleep -Seconds $RunSeconds
$c1 = Cycles; $t1 = Get-ActiveTab
Write-Output ('cycles in ' + $RunSeconds + 's : ' + ($c1 - $c0) + '   (expect ~' + [int]($RunSeconds/5) + ')')
Write-Output ('tab before / after: "' + $t0 + '"  ->  "' + $t1 + '"')

Write-Output ''
Write-Output '--- press Pause (must stop cycling) ---'
$root = Get-WidgetRoot
Click-Element $root 'Pause' | Write-Output
Start-Sleep -Seconds 1
$c2 = Cycles
Start-Sleep -Seconds $PauseSeconds
$c3 = Cycles
Write-Output ('cycles in ' + $PauseSeconds + 's while paused: ' + ($c3 - $c2) + '   (expect 0)')

Write-Output ''
Write-Output '--- log tail ---'
Get-Content -LiteralPath $log | Select-Object -Last 10
