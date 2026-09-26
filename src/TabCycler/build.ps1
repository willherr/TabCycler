# Builds TabCycler.exe.
#
#   pwsh -NoProfile -File .\build.ps1
#
# Compiles with the .NET Framework compiler that ships with Windows, so the
# build needs no SDK, no runtime install and no package restore. Also generates
# the application icon.

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

# ---- icon ------------------------------------------------------------------
Add-Type -AssemblyName System.Drawing

function New-TabCyclerIcon {
    param([int]$Size, [string]$Path)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode     = 'AntiAlias'
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad    = [Math]::Max(1.0, $Size * 0.055)
        $diam   = $Size - (2 * $pad)
        $accent = [System.Drawing.Color]::FromArgb(255, 0x2D, 0x7D, 0xD9)

        $brush = New-Object System.Drawing.SolidBrush($accent)
        $g.FillEllipse($brush, [single]$pad, [single]$pad, [single]$diam, [single]$diam)
        $brush.Dispose()

        # Open circular arrow: a 255 degree arc with a triangular head.
        $cx  = $Size / 2.0
        $cy  = $Size / 2.0
        $r   = $diam * 0.30
        $box = New-Object System.Drawing.RectangleF(
            [single]($cx - $r), [single]($cy - $r), [single]($r * 2), [single]($r * 2))

        $pen = New-Object System.Drawing.Pen(
                    [System.Drawing.Color]::White,
                    [single][Math]::Max(2.0, $Size * 0.115))
        $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
        $g.DrawArc($pen, $box, 205, 255)
        $pen.Dispose()

        $endDeg = 205 + 255.0
        $rad = $endDeg * [Math]::PI / 180.0
        $hx  = $cx + ($r * [Math]::Cos($rad))
        $hy  = $cy + ($r * [Math]::Sin($rad))
        $len = $diam * 0.30
        $wid = $diam * 0.20
        $tx = [Math]::Sin($rad)
        $ty = -[Math]::Cos($rad)
        $nx = [Math]::Cos($rad)
        $ny = [Math]::Sin($rad)

        $pts = New-Object 'System.Drawing.PointF[]' 3
        $pts[0] = New-Object System.Drawing.PointF([single]$hx, [single]$hy)
        $pts[1] = New-Object System.Drawing.PointF(
            [single]($hx - ($tx * $len) + ($nx * $wid)),
            [single]($hy - ($ty * $len) + ($ny * $wid)))
        $pts[2] = New-Object System.Drawing.PointF(
            [single]($hx - ($tx * $len) - ($nx * $wid)),
            [single]($hy - ($ty * $len) - ($ny * $wid)))

        $wb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $g.FillPolygon($wb, $pts)
        $wb.Dispose()
    }
    finally { $g.Dispose() }

    # Single 256px PNG-in-ICO entry; Windows scales it down for the taskbar.
    $ms = New-Object System.IO.MemoryStream
    try {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $ms.ToArray()
    }
    finally { $ms.Dispose(); $bmp.Dispose() }

    $fs = [System.IO.File]::Create($Path)
    try {
        $bw = New-Object System.IO.BinaryWriter($fs)
        $bw.Write([UInt16]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]1)
        $bw.Write([Byte]($Size -band 0xFF))
        $bw.Write([Byte]($Size -band 0xFF))
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$png.Length)
        $bw.Write([UInt32]22)
        $bw.Write($png)
    }
    finally { $fs.Dispose() }
}

$ico = Join-Path $PSScriptRoot 'TabCycler.ico'
New-TabCyclerIcon -Size 256 -Path $ico
Write-Host "icon: $ico"

# ---- compile ---------------------------------------------------------------
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found at $csc" }

$exe = Join-Path $PSScriptRoot 'TabCycler.exe'
if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }

$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File |
             ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "no .cs sources found in $PSScriptRoot" }

$compilerArgs = @(
    '/nologo'
    '/target:winexe'            # no console window on launch
    '/platform:anycpu'
    '/optimize+'
    '/warnaserror+'
    '/out:' + $exe
    '/win32icon:' + $ico
    '/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
) + $sources

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }
if (-not (Test-Path -LiteralPath $exe)) { throw "compile produced no exe" }

Write-Host "built: $exe"
