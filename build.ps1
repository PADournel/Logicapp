# Compile Logicapp.exe avec le compilateur C# fourni avec Windows (aucune installation requise).
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Drawing

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 16.0
    $C = [System.Drawing.Color]
    $small = $s -le 24

    # Fond : carré arrondi en dégradé indigo -> violet
    $bg = New-RoundRect (0.4 * $u) (0.4 * $u) (15.2 * $u) (15.2 * $u) (3.6 * $u)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), $C::FromArgb(255, 99, 102, 241), $C::FromArgb(255, 192, 77, 214)
    $g.FillPath($grad, $bg)
    # Reflet léger en haut
    $g.SetClip($bg)
    $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, ($s * 0.55)), $C::FromArgb(60, 255, 255, 255), $C::FromArgb(0, 255, 255, 255)
    $g.FillRectangle($shine, 0, 0, $s, $s * 0.55)
    $g.ResetClip()

    # La « zone » : carte blanche avec ombre, barre de titre et tuiles
    $cx = 2.7 * $u; $cy = 3.0 * $u; $cw = 10.6 * $u; $ch = 10.2 * $u; $cr = 1.7 * $u
    $shadow = New-RoundRect $cx ($cy + 0.5 * $u) $cw $ch $cr
    $g.FillPath((New-Object System.Drawing.SolidBrush ($C::FromArgb(70, 40, 20, 90))), $shadow)
    $card = New-RoundRect $cx $cy $cw $ch $cr
    $g.FillPath((New-Object System.Drawing.SolidBrush ($C::FromArgb(250, 255, 255, 255))), $card)
    $g.SetClip($card)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ($C::FromArgb(255, 67, 56, 202))), $cx, $cy, $cw, 2.4 * $u)
    $g.ResetClip()
    if (-not $small) {
        # Chevron (zone repliable) et nom stylisé dans la barre de titre
        $tri = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($cx + 1.0 * $u), ($cy + 0.85 * $u)),
            (New-Object System.Drawing.PointF ($cx + 2.2 * $u), ($cy + 0.85 * $u)),
            (New-Object System.Drawing.PointF ($cx + 1.6 * $u), ($cy + 1.6 * $u)))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush ($C::White)), $tri)
        $g.FillPath((New-Object System.Drawing.SolidBrush ($C::FromArgb(200, 255, 255, 255))), (New-RoundRect ($cx + 2.9 * $u) ($cy + 0.95 * $u) (4.2 * $u) (0.6 * $u) (0.3 * $u)))
    }
    # Tuiles (icônes rangées) : 2 x 2, couleurs vives
    $tiles = @($C::FromArgb(255, 255, 138, 101), $C::FromArgb(255, 56, 189, 248), $C::FromArgb(255, 74, 222, 128), $C::FromArgb(255, 250, 204, 21))
    $tw = 3.6 * $u; $th = 2.9 * $u; $gap = 0.9 * $u
    $tx0 = $cx + ($cw - 2 * $tw - $gap) / 2; $ty0 = $cy + 3.25 * $u
    for ($i = 0; $i -lt 4; $i++) {
        $tx = $tx0 + ($i % 2) * ($tw + $gap); $ty = $ty0 + [math]::Floor($i / 2) * ($th + $gap * 0.8)
        $g.FillPath((New-Object System.Drawing.SolidBrush $tiles[$i]), (New-RoundRect $tx $ty $tw $th (0.7 * $u)))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        # Grande taille : PNG (format standard pour 256 px)
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Petites tailles : bitmap 32 bits classique (lu par tous les programmes)
        $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
        $data = $bmp.LockBits($rect, 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
        $px = New-Object byte[] ($s * $s * 4)
        for ($y = 0; $y -lt $s; $y++) {
            [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $px, $y * $s * 4, $s * 4)
        }
        $bmp.UnlockBits($data)
        $maskRow = [int]([math]::Floor(($s + 31) / 32) * 4)
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $bw.Write([uint32]($s * $s * 4 + $maskRow * $s))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $s * 4, $s * 4) }   # lignes de bas en haut
        $bw.Write((New-Object byte[] ($maskRow * $s)))                               # masque AND vide
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

# Icône .ico multi-tailles (bitmaps 16 à 48 px + PNG 256 px)
$sizes = @(16, 24, 32, 48, 256)
$pngs = @()
foreach ($s in $sizes) { $pngs += , (New-IconPng $s) }
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $b = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[System.IO.File]::WriteAllBytes("$dir\icon.ico", $ms.ToArray())

# Arrête l'app si elle tourne, puis compile
Get-Process Logicapp, BureauZones -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    "/win32icon:$dir\icon.ico" "/win32manifest:$dir\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.VisualBasic.dll `
    "/out:$dir\Logicapp.exe" "$dir\Logicapp.cs"
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation" }
Write-Host "OK : $dir\Logicapp.exe"
