# Screen capture helper for verifying TabCycler.
#   .\screenshot.ps1                     full screen -> screenshot-full.png
#   .\screenshot.ps1 -Crop x,y,w,h       crop        -> screenshot-crop.png
param([string]$Crop)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$b  = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$out = Join-Path $PSScriptRoot 'screenshot-full.png'

if ($Crop) {
    $parts = $Crop.Split(',')
    $x = [int]$parts[0]; $y = [int]$parts[1]
    $w = [int]$parts[2]; $h = [int]$parts[3]
    $b = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $out = Join-Path $PSScriptRoot 'screenshot-crop.png'
}

$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
try {
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $g.Dispose(); $bmp.Dispose() }

Write-Output $out
