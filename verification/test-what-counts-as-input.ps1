# Which synthetic inputs actually update GetLastInputInfo?
# TabCycler decides "the user took over" from that timestamp, so any input
# that does not move it is invisible to the app.
# Idle duration = GetTickCount() - dwTime. Tick rate is taken from GetTickCount
# deltas over a known sleep, which is exact and immune to input interference.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Probe {
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(out LASTINPUTINFO p);
    [DllImport("kernel32.dll")] public static extern uint GetTickCount();
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int sz);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk,sc; public uint dwFlags,time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint msg; public ushort l,h; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MOUSEINPUT mo; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
    const uint INPUT_MOUSE=0, INPUT_KEYBOARD=1;
    const uint MOVE_REL=0x0001, LDOWN=0x0002, LUP=0x0004, ABSOLUTE=0x8000, VIRTUALDESK=0x4000;

    // Idle duration in milliseconds, given the measured tick rate.
    public static double IdleMs(double msPerTick) {
        LASTINPUTINFO l = new LASTINPUTINFO();
        l.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        GetLastInputInfo(out l);
        return (GetTickCount() - l.dwTime) * msPerTick;
    }
    static INPUT Mouse(int dx,int dy,uint f){ INPUT i=new INPUT(); i.type=INPUT_MOUSE; i.u.mo.dx=dx; i.u.mo.dy=dy; i.u.mo.dwFlags=f; return i; }
    static INPUT Key(ushort vk,uint f){ INPUT i=new INPUT(); i.type=INPUT_KEYBOARD; i.u.ki.vk=vk; i.u.ki.dwFlags=f; return i; }

    public static uint SetCursor(int x,int y){ return SetCursorPos(x,y) ? 1u : 0u; }
    public static uint MoveRelative(int dx,int dy){ INPUT[] b={Mouse(dx,dy,MOVE_REL)}; return SendInput(1,b,Marshal.SizeOf(typeof(INPUT))); }
    public static uint MoveAbsolute(int x,int y){
        int vx=(int)Math.Round(x/1920.0*65535.0), vy=(int)Math.Round(y/1080.0*65535.0);
        INPUT[] b={Mouse(vx,vy,MOVE_REL|ABSOLUTE|VIRTUALDESK)};
        return SendInput(1,b,Marshal.SizeOf(typeof(INPUT)));
    }
    public static uint Click(){ INPUT[] b={Mouse(0,0,LDOWN), Mouse(0,0,LUP)}; return SendInput(2,b,Marshal.SizeOf(typeof(INPUT))); }
    public static uint TapKey(ushort vk){ INPUT[] b={Key(vk,0), Key(vk,2)}; return SendInput(2,b,Marshal.SizeOf(typeof(INPUT))); }
}
'@ -ErrorAction Stop

# Exact tick rate from GetTickCount over a 2s sleep.
$g0 = [Probe]::GetTickCount(); Start-Sleep -Milliseconds 2000; $g1 = [Probe]::GetTickCount()
$msPerTick = 2000.0 / ($g1 - $g0)
Write-Output ('GetTickCount advanced ' + ($g1 - $g0) + ' over 2s -> tick period ' + [math]::Round($msPerTick, 4) + ' ms')
Write-Output ''

$p = New-Object Probe+POINT
[void][Probe]::GetCursorPos([ref]$p)
$cx = $p.X; $cy = $p.Y
Write-Output ('cursor parked at ' + $cx + ',' + $cy)
Write-Output ''

$cases = [ordered]@{
    'SetCursorPos +3px'            = { [Probe]::SetCursor($cx + 3, $cy) }
    'SendInput relative move +3px' = { [Probe]::MoveRelative(3, 0) }
    'SendInput absolute move'      = { [Probe]::MoveAbsolute($cx + 2, $cy) }
    'SendInput left click'         = { [Probe]::Click() }
    'SendInput key tap (F24)'      = { [Probe]::TapKey(0x7A) }
}

foreach ($label in $cases.Keys) {
    Start-Sleep -Milliseconds 2500                      # build a known idle window
    $before = [Probe]::IdleMs($msPerTick)
    $n = & $cases[$label]
    Start-Sleep -Milliseconds 150
    $after = [Probe]::IdleMs($msPerTick)
    $verdict = if ($after -lt 400) { 'DETECTED' } else { 'NOT seen' }
    Write-Output ($label.PadRight(32) + 'sent=' + $n + '  idle ' + [math]::Round($before) + 'ms -> ' + [math]::Round($after) + 'ms   ' + $verdict)
    [void][Probe]::SetCursor($cx, $cy)
}
