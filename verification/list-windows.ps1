# Read-only enumeration of visible top-level windows, to find anything that is
# genuinely full screen or unexpectedly sized. Touches nothing: no clicks, no
# focus changes, no synthetic input.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class Top {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

  public class Info { public string Proc, Title, Cls; public int L,T,W,H; public bool Full; }

  public static Info[] All() {
    var list = new List<Info>();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      RECT r; GetWindowRect(h, out r);
      int w = r.R - r.L, ht = r.B - r.T;
      if (w < 80 || ht < 40) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      string pn = "?"; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
      var t = new StringBuilder(200); GetWindowTextW(h, t, 200);
      var c = new StringBuilder(120); GetClassNameW(h, c, 120);
      list.Add(new Info { Proc = pn, Title = t.ToString(), Cls = c.ToString(),
                           L = r.L, T = r.T, W = w, H = ht,
                           Full = (r.L <= 0 && r.T <= 0 && w >= 1920 && ht >= 1080) });
      return true;
    }, IntPtr.Zero);
    return list.ToArray();
  }
}
'@

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Output ("monitor: $($bounds.Width)x$($bounds.Height)")
Write-Output ''
$all = [Top]::All() | Sort-Object -Property @{e={$_.W * $_.H}; descending=$true}
Write-Output ('{0,-20} {1,-11} {2,-46} {3}' -f 'PROCESS','SIZE','TITLE','CLASS')
foreach ($w in $all) {
    if ($w.W -lt 300) { continue }
    $flag = if ($w.Full) { '  <== FULL SCREEN' } else { '' }
    $title = $w.Title; if ($title.Length -gt 44) { $title = $title.Substring(0,44) }
    Write-Output ('{0,-20} {1,-11} {2,-46} {3}{4}' -f $w.Proc, "$($w.W)x$($w.H)", $title, $w.Cls, $flag)
}
Write-Output ''
$fs = @($all | Where-Object { $_.Full })
Write-Output ("windows covering the whole monitor: " + $fs.Count)
foreach ($f in $fs) { Write-Output ("   " + $f.Proc + "  [" + $f.Title + "]") }
