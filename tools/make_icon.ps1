param($Out)
Add-Type -AssemblyName System.Drawing
function DibOf($bmp, $s) {
  $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
  $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s*2)); $w.Write([UInt16]1); $w.Write([UInt16]32)
  $w.Write([UInt32]0); $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
  for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
  $rowBytes = [int]([Math]::Ceiling($s / 32.0) * 4)
  for ($y = 0; $y -lt $s; $y++) { $w.Write((New-Object byte[] $rowBytes)) }
  $w.Flush(); return ,($ms.ToArray())
}
$sizes = 16,24,32,48,64,128,256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([Drawing.Color]::Transparent)
  $r = [Math]::Max(2, [int]($s * 0.22)); $m = [Math]::Max(0, [int]($s * 0.03)); $w = $s - 2*$m
  $p = New-Object Drawing.Drawing2D.GraphicsPath
  $p.AddArc($m, $m, $r, $r, 180, 90); $p.AddArc($m+$w-$r, $m, $r, $r, 270, 90)
  $p.AddArc($m+$w-$r, $m+$w-$r, $r, $r, 0, 90); $p.AddArc($m, $m+$w-$r, $r, $r, 90, 90); $p.CloseFigure()
  $br = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0,0), (New-Object Drawing.Point $s,$s), ([Drawing.Color]::FromArgb(70,150,255)), ([Drawing.Color]::FromArgb(30,60,170))
  $g.FillPath($br, $p)
  $f = New-Object Drawing.Font "Segoe MDL2 Assets", ([float]($s * 0.62)), ([Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
  $rect = New-Object Drawing.RectangleF 0, ([float]($s*0.03)), $s, $s
  $g.DrawString([string][char]0xEA18, $f, [Drawing.Brushes]::White, $rect, $sf)   # shield
  $f2 = New-Object Drawing.Font "Segoe MDL2 Assets", ([float]($s * 0.26)), ([Drawing.GraphicsUnit]::Pixel)
  $b2 = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(30,60,170))
  if ($s -ge 24) { $g.DrawString([string][char]0xE72E, $f2, $b2, $rect, $sf) }       # lock inside
  $g.Dispose()
  if ($s -ge 256) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $pngs += ,($ms.ToArray()) } else { $pngs += ,(DibOf $bmp $s) }
  if ($s -eq 256) { $bmp.Save([IO.Path]::ChangeExtension($Out, ".png"), [Drawing.Imaging.ImageFormat]::Png) }
  $bmp.Dispose()
}
$fs = [IO.File]::Create($Out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $d = if ($s -ge 256) {0} else {$s}
  $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$off)
  $off += $pngs[$i].Length
}
foreach ($pb in $pngs) { $bw.Write($pb) }
$bw.Close()
"ok " + (Get-Item $Out).Length
