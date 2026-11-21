Add-Type -AssemblyName System.Drawing

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

$icoPath = "C:\Users\Administrator\Downloads\TypeRight!\src\TypeRight.Desktop\Resources\TypeRight.ico"
$bmp.Save($icoPath, [System.Drawing.Imaging.ImageFormat]::Icon)
$bmp.Dispose()

Write-Host "Created $icoPath"
