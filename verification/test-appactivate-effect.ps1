# Does AppActivate (used by every one of my test scripts) change the Windows
# Terminal window state? Records the full placement before and after, and
# distinguishes maximised from genuinely full screen.
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref PLACEMENT p);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] public struct PLACEMENT {
    public int length; public int flags; public int showCmd;
    public POINT ptMinPosition; public POINT ptMaxPosition; public RECT rcNormalPosition; }
}
'@
Add-Type -AssemblyName System.Windows.Forms
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
"primary working area (excludes taskbar): $($wa.Width)x$($wa.Height) at $($wa.X),$($wa.Y)"
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
"primary bounds (full monitor)          : $($bounds.Width)x$($bounds.Height)"
""

$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $wt) { 'no Windows Terminal window'; exit }

function Show-State($tag) {
    $r = New-Object W+RECT
    [void][W]::GetWindowRect($wt.MainWindowHandle, [ref]$r)
    $pl = New-Object W+PLACEMENT
    $pl.length = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'W+PLACEMENT')
    [void][W]::GetWindowPlacement($wt.MainWindowHandle, [ref]$pl)
    $show = switch ($pl.showCmd) { 1 {'SW_SHOWNORMAL'} 3 {'SW_SHOWMAXIMIZED'} default {"showCmd=$($pl.showCmd)"} }
    $covers = ($r.L -le 0 -and $r.T -le 0 -and $r.R -ge $bounds.Width -and $r.B -ge $bounds.Height)
    $verdict = if ($covers -and $pl.showCmd -ne 3) { 'FULL SCREEN (taskbar hidden)' }
               elseif ($pl.showCmd -eq 3) { 'maximised (taskbar visible)' }
               else { 'normal/restored' }
    Write-Output ("{0,-18} rect={1},{2}-{3},{4}  {5,-18} zoomed={6,-5} -> {7}" -f `
        $tag, $r.L,$r.T,$r.R,$r.B, $show, [W]::IsZoomed($wt.MainWindowHandle), $verdict)
}

Show-State 'before'
[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Milliseconds 600
Show-State 'after AppActivate'
Start-Sleep -Seconds 2
Show-State '2s later'
