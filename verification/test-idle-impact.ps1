# Does an injected Ctrl+Tab reset Windows' global "last input" timestamp?
#
# GetLastInputInfo().dwTime is an ABSOLUTE tick stamp, not a counter, so idle
# duration = GetTickCount() - dwTime (both in the same tick units). Measuring
# the delta between two Tick() calls tells you nothing about the idle counter,
# which is the trap in the first version of this test.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Idle {
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(out LASTINPUTINFO p);
    [DllImport("kernel32.dll")] static extern uint GetTickCount();
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint m,f,t; public IntPtr e; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint f,t; public IntPtr e; }
    [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint m; public ushort l,h; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
    static INPUT K(ushort vk, uint f){ INPUT i=new INPUT(); i.type=1; i.u.ki.vk=vk; i.u.ki.f=f; return i; }

    // Idle duration in raw tick units, derived from the absolute stamp.
    public static double IdleTicks() {
        LASTINPUTINFO l = new LASTINPUTINFO();
        l.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        GetLastInputInfo(out l);
        return (double)(GetTickCount() - l.dwTime);
    }
    public static uint SendCtrlTab() {
        INPUT[] b = new INPUT[4];
        b[0]=K(0x11,0); b[1]=K(0x09,0); b[2]=K(0x09,2); b[3]=K(0x11,2);
        return SendInput(4, b, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@ -ErrorAction Stop

# Work out the tick period empirically instead of assuming 10ms.
$t0 = [Idle]::IdleTicks(); $g0 = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Milliseconds 2000
$g0.Stop()
$ticksIn2s = [Idle]::IdleTicks() - $t0
$msPerTick = 2000.0 / $ticksIn2s
Write-Output ('tick period measured at ' + [math]::Round($msPerTick, 4) + ' ms  (' + [math]::Round($ticksIn2s) + ' ticks in 2s)')
Write-Output ''

# Establish a known idle window, then see if the injection clears it.
Write-Output 'Step 1: sit idle for 4s to build up idle time'
Start-Sleep -Seconds 4
$idleBefore = [Idle]::IdleTicks()
Write-Output ('        idle before send = ' + [math]::Round($idleBefore * $msPerTick) + ' ms')

Write-Output 'Step 2: inject Ctrl+Tab (the exact thing TabCycler does)'
$sent = [Idle]::SendCtrlTab()
$idleAfter = [Idle]::IdleTicks()
Write-Output ('        SendInput queued  = ' + $sent + ' of 4')
Write-Output ('        idle after send  = ' + [math]::Round($idleAfter * $msPerTick) + ' ms')
Write-Output ''

if ($sent -ne 4) {
    Write-Output 'RESULT: send did not go through, inconclusive.'
} elseif ($idleAfter -lt ($idleBefore * 0.2)) {
    Write-Output 'RESULT: YES - the injection reset the global idle timer.'
    Write-Output '        Teams/presence reading idle time would see fresh activity'
    Write-Output '        every 5s, so it would NOT show you as Away.'
} else {
    Write-Output 'RESULT: NO - the injection left the idle timer alone.'
    Write-Output '        Presence systems would still correctly see you as idle.'
}
