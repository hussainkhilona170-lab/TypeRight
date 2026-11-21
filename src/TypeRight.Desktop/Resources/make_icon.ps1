# Generates a valid ICO file with a simple 32x32 bitmap (no external dependencies)
# ICO format: header + directory entry + BMP (BITMAPINFOHEADER + XOR mask + AND mask)

$size = 32
$output = "C:\Users\Administrator\Downloads\TypeRight!\src\TypeRight.Desktop\Resources\TypeRight.ico"

# BMP info header (40 bytes)
$bmpHeader = New-Object byte[] 40
function Write-Int32LE([byte[]]$buf, [int]$offset, [int]$value) {
    $buf[$offset] = [byte]($value -band 0xFF)
    $buf[$offset+1] = [byte](($value -shr 8) -band 0xFF)
    $buf[$offset+2] = [byte](($value -shr 16) -band 0xFF)
    $buf[$offset+3] = [byte](($value -shr 24) -band 0xFF)
}
function Write-UInt16LE([byte[]]$buf, [int]$offset, [int]$value) {
    $buf[$offset] = [byte]($value -band 0xFF)
    $buf[$offset+1] = [byte](($value -shr 8) -band 0xFF)
}

Write-Int32LE $bmpHeader 0 40          # biSize
Write-Int32LE $bmpHeader 4 $size       # biWidth
Write-Int32LE $bmpHeader 8 ($size * 2) # biHeight (double for XOR + AND masks)
Write-UInt16LE $bmpHeader 12 1         # biPlanes
Write-UInt16LE $bmpHeader 14 32        # biBitCount
Write-Int32LE $bmpHeader 16 0          # biCompression BI_RGB
Write-Int32LE $bmpHeader 20 0          # biSizeImage

# XOR mask: 32bpp BGRA, bottom-up rows
$xor = New-Object byte[] ($size * $size * 4)
$r = 0x2C; $g = 0x3E; $b = 0x50
for ($y = 0; $y -lt $size; $y++) {
    for ($x = 0; $x -lt $size; $x++) {
        $idx = ($y * $size + $x) * 4
        $isBg = ($x -ge 3 -and $x -le 28 -and $y -ge 3 -and $y -le 28)
        if ($isBg) {
            # Background color (BGRA)
            $xor[$idx] = $b; $xor[$idx+1] = $g; $xor[$idx+2] = $r; $xor[$idx+3] = 0xFF
            # White "T" shape: horizontal bar y 8-11, vertical bar x 14-17 y 8-24
            if (($y -ge 8 -and $y -le 11 -and $x -ge 8 -and $x -le 23) -or
                ($x -ge 14 -and $x -le 17 -and $y -ge 8 -and $y -le 24)) {
                $xor[$idx] = 0xFF; $xor[$idx+1] = 0xFF; $xor[$idx+2] = 0xFF; $xor[$idx+3] = 0xFF
            }
        } else {
            $xor[$idx] = 0; $xor[$idx+1] = 0; $xor[$idx+2] = 0; $xor[$idx+3] = 0
        }
    }
}

# AND mask: 1bpp, rows padded to 4 bytes. 32 pixels = 4 bytes per row.
$andRowBytes = 4
$and = New-Object byte[] ($andRowBytes * $size)
# All zeros = fully opaque based on alpha in XOR

# Combine BMP data
$bmpData = [byte[]]::new(40 + $xor.Length + $and.Length)
[Array]::Copy($bmpHeader, 0, $bmpData, 0, 40)
[Array]::Copy($xor, 0, $bmpData, 40, $xor.Length)
[Array]::Copy($and, 0, $bmpData, 40 + $xor.Length, $and.Length)

# ICONDIR (6 bytes)
$iconDir = New-Object byte[] 6
Write-UInt16LE $iconDir 0 0   # reserved
Write-UInt16LE $iconDir 2 1   # type: icon
Write-UInt16LE $iconDir 4 1   # count

# ICONDIRENTRY (16 bytes)
$entry = New-Object byte[] 16
$entry[0] = 32  # width
$entry[1] = 32  # height
$entry[2] = 0   # colors
$entry[3] = 0   # reserved
Write-UInt16LE $entry 4 1    # planes
Write-UInt16LE $entry 6 32   # bit count
Write-Int32LE $entry 8 $bmpData.Length  # size of image data
Write-Int32LE $entry 12 22   # offset (6 + 16)

# Write final ICO
$ico = [byte[]]::new(6 + 16 + $bmpData.Length)
[Array]::Copy($iconDir, 0, $ico, 0, 6)
[Array]::Copy($entry, 0, $ico, 6, 16)
[Array]::Copy($bmpData, 0, $ico, 22, $bmpData.Length)

[System.IO.File]::WriteAllBytes($output, $ico)
Write-Host "Created valid ICO: $output ($($ico.Length) bytes)"
