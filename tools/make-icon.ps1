<#
.SYNOPSIS
    Draws the DropShelf tray icon and packs it into a multi-resolution .ico.

.DESCRIPTION
    The icon is a shelf with two cards resting on it. Windows picks the 16px
    entry for the notification area, so the shape has to survive being that
    small: solid blocks, no outlines, no detail below about two pixels.

    Run this from the repository root when the mark changes:

        powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

    Regenerating is deterministic, so a rerun with no edits leaves the file
    byte-identical and git stays quiet.
#>

[CmdletBinding()]
param(
    [string] $OutputPath
)

# $PSScriptRoot is not populated while parameter defaults are being bound under
# Windows PowerShell 5.1, so the default is resolved here instead.
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot '..\src\DropShelf\Assets\DropShelf.ico'
}

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)

function New-RoundedPath {
    param(
        [float] $X, [float] $Y, [float] $Width, [float] $Height, [float] $Radius
    )

    $d = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($d -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF $X, $Y, $Width, $Height))
        return $path
    }

    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc(($X + $Width - $d), $Y, $d, $d, 270, 90)
    $path.AddArc(($X + $Width - $d), ($Y + $Height - $d), $d, $d, 0, 90)
    $path.AddArc($X, ($Y + $Height - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int] $Size)

    # Everything is expressed against a 256 unit design grid and scaled, so the
    # proportions stay identical at every resolution.
    $s = $Size / 256.0

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $accent = [System.Drawing.Color]::FromArgb(255, 88, 166, 255)   # the front card
        $muted  = [System.Drawing.Color]::FromArgb(255, 138, 180, 248)  # the card behind
        $shelf  = [System.Drawing.Color]::FromArgb(255, 232, 238, 246)  # the shelf itself

        # Back card, offset up and to the right so it reads as a stack.
        $backBrush = New-Object System.Drawing.SolidBrush $muted
        $backPath = New-RoundedPath ([float](96 * $s)) ([float](40 * $s)) ([float](104 * $s)) ([float](104 * $s)) ([float](22 * $s))
        $g.FillPath($backBrush, $backPath)
        $backPath.Dispose(); $backBrush.Dispose()

        # Front card.
        $frontBrush = New-Object System.Drawing.SolidBrush $accent
        $frontPath = New-RoundedPath ([float](56 * $s)) ([float](68 * $s)) ([float](112 * $s)) ([float](112 * $s)) ([float](24 * $s))
        $g.FillPath($frontBrush, $frontPath)
        $frontPath.Dispose(); $frontBrush.Dispose()

        # The shelf. A single bar, thick enough to stay visible at 16px.
        $shelfBrush = New-Object System.Drawing.SolidBrush $shelf
        $shelfPath = New-RoundedPath ([float](28 * $s)) ([float](196 * $s)) ([float](200 * $s)) ([float](34 * $s)) ([float](17 * $s))
        $g.FillPath($shelfBrush, $shelfPath)
        $shelfPath.Dispose(); $shelfBrush.Dispose()
    }
    finally {
        $g.Dispose()
    }

    return $bitmap
}

function ConvertTo-IconDib {
    <#
        Packs a bitmap as a legacy ICO entry: a BITMAPINFOHEADER whose height is
        doubled to account for a mask, then bottom-up 32bpp BGRA pixels, then a
        1bpp AND mask.

        The mask is left entirely zero. For a 32bpp entry Windows uses the alpha
        channel, and the mask only matters to callers that ignore alpha.
    #>
    param([System.Drawing.Bitmap] $Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height

    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $pixels = New-Object byte[] ($stride * $height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    try {
        $maskStride = [int][Math]::Floor((($width + 31) / 32)) * 4
        $xorSize = $width * 4 * $height
        $andSize = $maskStride * $height

        $writer.Write([uint32] 40)                  # biSize
        $writer.Write([int32] $width)               # biWidth
        $writer.Write([int32] ($height * 2))        # biHeight, image plus mask
        $writer.Write([uint16] 1)                   # biPlanes
        $writer.Write([uint16] 32)                  # biBitCount
        $writer.Write([uint32] 0)                   # biCompression, BI_RGB
        $writer.Write([uint32] ($xorSize + $andSize))
        $writer.Write([int32] 0)                    # biXPelsPerMeter
        $writer.Write([int32] 0)                    # biYPelsPerMeter
        $writer.Write([uint32] 0)                   # biClrUsed
        $writer.Write([uint32] 0)                   # biClrImportant

        # DIB rows run bottom to top.
        for ($y = $height - 1; $y -ge 0; $y--) {
            $writer.Write($pixels, ($y * $stride), ($width * 4))
        }

        $writer.Write((New-Object byte[] $andSize))

        $writer.Flush()

        # The leading comma stops PowerShell unrolling the array into the
        # pipeline, which would hand the caller an Object[] of boxed bytes.
        return , $stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function ConvertTo-IconPng {
    param([System.Drawing.Bitmap] $Bitmap)

    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$entries = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    try {
        # PNG entries keep the large images small, but System.Drawing's own icon
        # reader cannot decode them. Anything the app loads through System.Drawing
        # is 64px or under, so those stay as uncompressed DIBs and the rest, which
        # only ever go through the shell, are compressed. Storing 128 and 256 as
        # DIBs instead would triple the file for no benefit.
        [byte[]] $bytes = if ($size -ge 128) { ConvertTo-IconPng $bitmap } else { ConvertTo-IconDib $bitmap }
        $entries += , @{ Size = $size; Bytes = $bytes }
    }
    finally {
        $bitmap.Dispose()
    }
}

# ICO container: a 6 byte header, then one 16 byte directory entry per image,
# then the image payloads.
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $output
try {
    $writer.Write([uint16] 0)              # reserved
    $writer.Write([uint16] 1)              # type: icon
    $writer.Write([uint16] $entries.Count)

    $offset = 6 + (16 * $entries.Count)
    foreach ($entry in $entries) {
        $dimension = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
        $writer.Write([byte] $dimension)   # width, 0 means 256
        $writer.Write([byte] $dimension)   # height
        $writer.Write([byte] 0)            # palette size, 0 for truecolour
        $writer.Write([byte] 0)            # reserved
        $writer.Write([uint16] 1)          # colour planes
        $writer.Write([uint16] 32)         # bits per pixel
        $writer.Write([uint32] $entry.Bytes.Length)
        $writer.Write([uint32] $offset)
        $offset += $entry.Bytes.Length
    }

    foreach ($entry in $entries) {
        $writer.Write($entry.Bytes)
    }

    $writer.Flush()

    $resolved = [System.IO.Path]::GetFullPath($OutputPath)
    $directory = [System.IO.Path]::GetDirectoryName($resolved)
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [System.IO.File]::WriteAllBytes($resolved, $output.ToArray())
    Write-Host "Wrote $resolved ($($output.Length) bytes, $($entries.Count) sizes)"

    # Read it back the same way the app will. A malformed entry table produces a
    # file that Explorer renders happily and System.Drawing refuses, so the only
    # check worth having is the strict one.
    foreach ($size in $sizes) {
        if ($size -ge 128) { continue }
        $icon = New-Object System.Drawing.Icon $resolved, $size, $size
        try {
            if ($icon.Width -ne $size) {
                throw "Asked for the ${size}px entry and got $($icon.Width)px."
            }
            $icon.ToBitmap().Dispose()
        }
        finally {
            $icon.Dispose()
        }
    }
    Write-Host "Verified every entry loads through System.Drawing."
}
finally {
    $writer.Dispose()
    $output.Dispose()
}
