Add-Type -AssemblyName System.Drawing

# Save an ICO through a real HICON. Bitmap.Save(..., ImageFormat.Icon) is not
# supported by GDI+ and throws, so build the icon from the bitmap's HICON.
if (-not ('TypeRightIconNative' -as [type])) {
  Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TypeRightIconNative {
  [DllImport("user32.dll", SetLastError = true)]
  public static extern bool DestroyIcon(IntPtr hIcon);
}
"@
}

$icoPath = Join-Path $PSScriptRoot "TypeRight.ico"

$bmp = New-Object System.Drawing.Bitmap 64, 64
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Background rounded square
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 44, 62, 80))
$g.FillRectangle($brush, 4, 4, 56, 56)

# White "T" glyph
$fontFamily = New-Object System.Drawing.FontFamily "Segoe UI"
$font = New-Object System.Drawing.Font ($fontFamily, 34, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$rect = New-Object System.Drawing.RectangleF(4, 4, 56, 56)
$g.DrawString("T", $font, $white, $rect, $sf)

$g.Dispose()
$font.Dispose()
$fontFamily.Dispose()
$brush.Dispose()
$white.Dispose()
$sf.Dispose()

$hicon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hicon)
try {
  $stream = New-Object System.IO.MemoryStream
  try {
    $icon.Save($stream)
    [System.IO.File]::WriteAllBytes($icoPath, $stream.ToArray())
  }
  finally {
    $stream.Dispose()
  }
}
finally {
  $icon.Dispose()
  [void][TypeRightIconNative]::DestroyIcon($hicon)
  $bmp.Dispose()
}

Write-Host "Created $icoPath"
