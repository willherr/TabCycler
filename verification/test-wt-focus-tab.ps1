# Measures the wt focus-tab approach against the four things that matter for
# replacing the injected Ctrl+Tab:
#   1. does it actually switch tabs?
#   2. does it avoid creating a window?
#   3. does it leave the global idle timestamp alone (so Teams can see idle)?
#   4. what does it cost per cycle?
param([int]$TargetIndex = 1, [int]$IdleBuildupSeconds = 4)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class M {
  [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(out LASTINPUTINFO p);
  [DllImport("kernel32.dll")] static extern uint GetTickCount();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public static double IdleMs(){ LASTINPUTINFO l=new LASTINPUTINFO(); l.cbSize=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO)); GetLastInputInfo(out l); return (GetTickCount()-l.dwTime)*0.99; }
  public static string Fg(){
    IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero) return "(none)";
    uint pid; GetWindowThreadProcessId(h,out pid);
    try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
  }
}
'@

function Get-Windows {
    @(Get-Process -Name WindowsTerminal -ErrorAction SilentlyContinue |
      Where-Object { $_.MainWindowHandle -ne 0 })
}
function Get-TabInfo {
    $wt = Get-Windows | Select-Object -First 1
    if (-not $wt) { return $null }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($wt.MainWindowHandle)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $selected = -1; $names = @()
    for ($i = 0; $i -lt $tabs.Count; $i++) {
        $names += $tabs[$i].Current.Name
        try {
            $pat = $tabs[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($pat.Current.IsSelected) { $selected = $i }
        } catch {}
    }
    [pscustomobject]@{ Count = $tabs.Count; Selected = $selected; Title = $(if ($selected -ge 0) { $names[$selected] } else { '?' }) }
}

$before = Get-TabInfo
$winsBefore = (Get-Windows).Count

# Build up a real idle window first. The first run of this test reported
# "idle 0ms" before the command even started, which made the after-reading
# meaningless: it could not have distinguished a reset from an already-idle
# machine. If idle is not genuinely built up, say so rather than guess.
Write-Output "building up idle for ${IdleBuildupSeconds}s (do not touch the machine)"
Start-Sleep -Seconds $IdleBuildupSeconds
$idleBefore = [M]::IdleMs()

Write-Output ("before: {0} tab(s), selected index {1} [{2}], {3} window(s), idle {4}ms, fg {5}" -f `
    $before.Count, $before.Selected, $before.Title, $winsBefore, [math]::Round($idleBefore), [M]::Fg())

if ($idleBefore -lt 1500) {
    Write-Output ""
    Write-Output "SKIPPED: idle only reached $([math]::Round($idleBefore))ms, so something is"
    Write-Output "generating input on this machine and the test cannot be trusted."
    exit 2
}

Write-Output ""
Write-Output "running: wt -w last focus-tab -t $TargetIndex"
$sw = [Diagnostics.Stopwatch]::StartNew()
& wt -w last focus-tab -t $TargetIndex
$sw.Stop()
Start-Sleep -Milliseconds 900

$after = Get-TabInfo
$winsAfter = (Get-Windows).Count
$idleAfter = [M]::IdleMs()
Write-Output ""
Write-Output ("elapsed       : {0} ms" -f [math]::Round($sw.Elapsed.TotalMilliseconds))
Write-Output ("after : {0} tab(s), selected index {1} [{2}], {3} window(s), idle {4}ms, fg {5}" -f `
    $after.Count, $after.Selected, $after.Title, $winsAfter, [math]::Round($idleAfter), [M]::Fg())
Write-Output ""
Write-Output ("tab changed   : {0}" -f $(if ($before.Selected -ne $after.Selected) { "YES ($($before.Selected) -> $($after.Selected))" } else { "no" }))
Write-Output ("new window    : {0}" -f $(if ($winsAfter -gt $winsBefore) { "YES, $winsBefore -> $winsAfter  <-- BAD" } else { "no" }))
Write-Output ("idle before   : {0}ms" -f [math]::Round($idleBefore))
Write-Output ("idle after    : {0}ms" -f [math]::Round($idleAfter))
if ($idleAfter -lt ($idleBefore * 0.2)) {
    Write-Output ("idle verdict  : RESET  <-- the Teams problem would remain")
} else {
    Write-Output ("idle verdict  : left alone  <-- fixes the Teams problem")
}
Write-Output ("focus moved   : {0}" -f [M]::Fg())
