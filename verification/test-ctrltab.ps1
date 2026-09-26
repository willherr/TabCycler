# Diagnostic: confirm the Ctrl+Tab mechanism actually switches the Windows
# Terminal tab. Prints foreground window identity at each step so a failure can
# be attributed to focus or to the keystroke itself.
param(
    [int]$Cycles = 3,
    [int]$GapMs  = 1200
)

Add-Type -AssemblyName Microsoft.VisualBasic

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Probe {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint m,f,t; public IntPtr e; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint f,t; public IntPtr e; }
    [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint m; public ushort l,h; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }

    public static string Fg() {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return "(none)";
        uint pid; GetWindowThreadProcessId(h, out pid);
        string pn = "?";
        try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
        return pn;
    }
    public static int SendCtrlTab() {
        int sz = Marshal.SizeOf(typeof(INPUT));
        INPUT[] b = new INPUT[4];
        b[0]=K(0x11,0); b[1]=K(0x09,0); b[2]=K(0x09,2); b[3]=K(0x11,2);
        return (int)SendInput(4, b, sz);
    }
    static INPUT K(ushort vk, uint f) {
        INPUT i = new INPUT(); i.type = 1; i.u.ki.vk = vk; i.u.ki.f = f; return i;
    }
}
'@

$probe = Join-Path $PSScriptRoot 'get-active-tab.ps1'
function Get-ActiveTab { (& pwsh -NoProfile -File $probe) }

$wt = Get-Process -Name WindowsTerminal | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $wt) { Write-Output 'NO-WT'; exit 1 }

[Microsoft.VisualBasic.Interaction]::AppActivate($wt.Id) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output ('start fg = ' + [Probe]::Fg())

for ($i = 1; $i -le $Cycles; $i++) {
    $before = Get-ActiveTab
    $sent   = [Probe]::SendCtrlTab()
    Start-Sleep -Milliseconds $GapMs
    $after  = Get-ActiveTab
    $verdict = if ($before -ne $after) { 'CHANGED' } else { 'SAME' }
    Write-Output ('cycle ' + $i + ': fg=' + [Probe]::Fg() + ' sent=' + $sent +
                  '  [' + $before + '] -> [' + $after + ']  ' + $verdict)
    Start-Sleep -Milliseconds 400
}
