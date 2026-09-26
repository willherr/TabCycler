# Measures the pixel height of rendered text in a screen region, so the real
# font scale can be measured rather than inferred from what a control "should"
# be. Reports the vertical extent of pixels that differ from the background.
param(
    [int]$X, [int]$Y, [int]$W, [int]$H,
    [int]$BgR = 31, [int]$BgG = 31, [int]$BgB = 31,
    [int]$Tolerance = 18
)

Add-Type -AssemblyName System.Drawing

$bmp = New-Object System.Drawing.Bitmap($W, $H,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
try {
    $g.CopyFromScreen($X, $Y, 0, 0, $bmp.Size)

    $rows = @()
    for ($y = 0; $y -lt $H; $y++) {
        $ink = 0
        for ($x = 0; $x -lt $W; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $d = [Math]::Abs($c.R - $BgR) + [Math]::Abs($c.G - $BgG) + [Math]::Abs($c.B - $BgB)
            if ($d -gt $Tolerance) { $ink++ }
        }
        if ($ink -gt 0) { $rows += $y }
    }
}
finally { $g.Dispose(); $bmp.Dispose() }

if ($rows.Count -eq 0) {
    Write-Output "no text pixels found in ${X},${Y} ${W}x${H}"
} else {
    $top = $rows[0]; $bot = $rows[-1]
    Write-Output ("region ${X},${Y} ${W}x${H}")
    Write-Output ("ink rows: $($rows.Count)  from y=$top to y=$bot  => text height " + ($bot - $top + 1) + " px")
}
