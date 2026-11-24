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
    # Dessin « Orbite » sur une grille 64 x 64 (reprend les orbites du preset Exil)
    $u = $s / 64.0
    $C = [System.Drawing.Color]
    $P = [System.Drawing.PointF]
    $small = $s -le 24
    $copperA = $C::FromArgb(255, 240, 178, 122)
    $copperB = $C::FromArgb(255, 184, 103, 58)
    $coal = $C::FromArgb(255, 27, 29, 34)

    # Fond : disque charbon
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $coal), (2 * $u), (2 * $u), (60 * $u), (60 * $u))

    # Orbites cuivrées (une seule, plus épaisse, aux petites tailles)
    $orbits = @(@(-25, 204), @(35, 115))
    if ($small) { $orbits[0][1] = 230 }
    $nOrbits = if ($small) { 1 } else { 2 }
    $penW = [math]::Max(1.6 * $u, 1.1)
    for ($k = 0; $k -lt $nOrbits; $k++) {
        $o = $orbits[$k]
        $ob = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object $P (8 * $u), (8 * $u)), (New-Object $P (56 * $u), (56 * $u)), $C::FromArgb($o[1], $copperA), $C::FromArgb($o[1], $copperB)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddEllipse((8 * $u), (22 * $u), (48 * $u), (20 * $u))
        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.RotateAt($o[0], (New-Object $P (32 * $u), (32 * $u)))
        $path.Transform($m)
        $g.DrawPath((New-Object System.Drawing.Pen $ob, $penW), $path)
    }

    # La zone : carré arrondi cuivré au centre, avec 4 icônes (la dernière estompée)
    $zb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object $P (22 * $u), (22 * $u)), (New-Object $P (42 * $u), (42 * $u)), $copperA, $copperB
    $g.FillPath($zb, (New-RoundRect (22 * $u) (22 * $u) (20 * $u) (20 * $u) (5 * $u)))
    if (-not $small) {
        $cells = @(@(27, 27, 255), @(33, 27, 255), @(27, 33, 255), @(33, 33, 102))
        foreach ($cell in $cells) {
            $g.FillPath((New-Object System.Drawing.SolidBrush ($C::FromArgb($cell[2], $coal))), (New-RoundRect ($cell[0] * $u) ($cell[1] * $u) (4 * $u) (4 * $u) (1 * $u)))
        }
    }

    # Satellites sur les orbites
    $r1 = [math]::Max(3 * $u, 1.1); $r2 = [math]::Max(2.2 * $u, 0.9)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $copperA), (53 * $u - $r1), (22 * $u - $r1), (2 * $r1), (2 * $r1))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ($C::White)), (13 * $u - $r2), (43 * $u - $r2), (2 * $r2), (2 * $r2))
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
