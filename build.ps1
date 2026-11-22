# Compile BureauZones.exe avec le compilateur C# fourni avec Windows (aucune installation requise).
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
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 16.0
    $body = New-RoundRect (0.5 * $u) (1.5 * $u) (15 * $u) (13 * $u) (2.2 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 40, 44, 56))), $body)
    $g.SetClip($body)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 74, 144, 226))), 0, 0, $s, 5 * $u)
    $g.ResetClip()
    $tile = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 236, 240, 246))
    foreach ($ty in @(7.2, 10.8)) { foreach ($tx in @(2.6, 6.75, 10.9)) {
        $g.FillRectangle($tile, [float]($tx * $u), [float]($ty * $u), [float](2.5 * $u), [float](2.4 * $u))
    } }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# Icône .ico multi-tailles (PNG intégrés)
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
Get-Process BureauZones -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    "/win32icon:$dir\icon.ico" "/win32manifest:$dir\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.VisualBasic.dll `
    "/out:$dir\BureauZones.exe" "$dir\BureauZones.cs"
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation" }
Write-Host "OK : $dir\BureauZones.exe"
