# Standalone probe for the low-level input hook. Installs the same
# WH_KEYBOARD_LL / WH_MOUSE_LL hook the widget uses and reports what it
# observes, grouped by kind and by whether the event was injected.
#
# It never injects anything itself, so running it does not disturb a live
# desktop. It exists to prove the hook delivers events, and to show what is
# generating input on this machine.
param([int]$Seconds = 15)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public sealed class HookProbe : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public int X, Y; public uint mouseData, flags, time; public IntPtr extra; }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint tid);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string m);

    IntPtr _kb = IntPtr.Zero, _ms = IntPtr.Zero;
    readonly HookProc _kbProc, _msProc;   // kept alive deliberately
    public readonly List<string> Events = new List<string>();
    public bool KeyboardInstalled { get { return _kb != IntPtr.Zero; } }
    public bool MouseInstalled { get { return _ms != IntPtr.Zero; } }

    public HookProbe()
    {
        _kbProc = KeyCb;
        _msProc = MouseCb;
        IntPtr mod = GetModuleHandle(null);
        _kb = SetWindowsHookEx(13, _kbProc, mod, 0);
        _ms = SetWindowsHookEx(14, _msProc, mod, 0);
    }

    IntPtr KeyCb(int nCode, IntPtr w, IntPtr l)
    {
        try
        {
            if (nCode >= 0 && (w.ToInt32() == 0x0100 || w.ToInt32() == 0x0104))
            {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(KBDLLHOOKSTRUCT));
                string vk = KeyName(k.vkCode);
                Record("key  " + vk + (k.flags != 0 ? "  flags=" + k.flags.ToString("X") : ""));
            }
        }
        catch { }
        return CallNextHookEx(_kb, nCode, w, l);
    }

    IntPtr MouseCb(int nCode, IntPtr w, IntPtr l)
    {
        try
        {
            if (nCode >= 0)
            {
                var m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(MSLLHOOKSTRUCT));
                int msg = w.ToInt32();
                string kind =
                    msg == 0x0200 ? "move" :
                    (msg == 0x0201 || msg == 0x0204 || msg == 0x0207 || msg == 0x020B) ? "click" :
                    (msg == 0x020A || msg == 0x020E) ? "scroll" : null;
                if (kind != null) Record(kind + "  at " + m.X + "," + m.Y + (m.flags != 0 ? "  flags=" + m.flags.ToString("X") : ""));
            }
        }
        catch { }
        return CallNextHookEx(_ms, nCode, w, l);
    }

    void Record(string s) { if (Events.Count < 2000) Events.Add(s); }

    static string KeyName(uint vk)
    {
        if (vk >= 0x30 && vk <= 0x39) return "digit " + (char)('0' + (vk - 0x30));
        if (vk >= 0x41 && vk <= 0x5A) return "letter " + (char)vk;
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        switch (vk)
        {
            case 0x20: return "SPACE";
            case 0x0D: return "ENTER";
            case 0x09: return "TAB";
            case 0x1B: return "ESC";
            case 0xA0: case 0xA1: return "SHIFT";
            case 0xA2: case 0xA3: return "CTRL";
            case 0xA4: case 0xA5: return "WIN";
            case 0xA6: case 0xA7: return "ALT";
        }
        return "VK_" + vk.ToString("X2");
    }

    public void Dispose()
    {
        if (_kb != IntPtr.Zero) { UnhookWindowsHookEx(_kb); _kb = IntPtr.Zero; }
        if (_ms != IntPtr.Zero) { UnhookWindowsHookEx(_ms); _ms = IntPtr.Zero; }
    }
}
'@

Write-Output "installing the hook, observing for $Seconds seconds (touch nothing)"

$probe = New-Object HookProbe
Write-Output ("keyboard hook installed: " + $probe.KeyboardInstalled)
Write-Output ("mouse hook installed   : " + $probe.MouseInstalled)
Write-Output ""

$start = Get-Date
while (((Get-Date) - $start).TotalSeconds -lt $Seconds) {
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.Application]::DoEvents()
}

$events = $probe.Events
$probe.Dispose()

Write-Output ("events observed: " + $events.Count)
if ($events.Count -eq 0) {
    Write-Output ""
    Write-Output "NOTHING was observed. The hook installed but delivered no events, which"
    Write-Output "means it is not working and must not be relied on."
    exit 2
}

Write-Output ""
Write-Output "=== grouped ==="
$events | ForEach-Object {
    $kind = ($_ -split '\s+')[0]
    $inj  = if ($_ -match 'flags=[0-9A-F]*1') { 'INJECTED' } else { 'real?' }
    [pscustomobject]@{ Kind = $kind; Source = $inj }
} | Group-Object Kind, Source | Sort-Object Count -Descending |
    ForEach-Object { "  {0,5}  {1}" -f $_.Count, $_.Name }

Write-Output ""
Write-Output "=== first 12 raw ==="
$events | Select-Object -First 12 | ForEach-Object { "  $_" }
