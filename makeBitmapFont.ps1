param(
    [Parameter(Mandatory = $true)][string]$FontSource,
    [Parameter(Mandatory = $true)][string]$OutBase,
    [Parameter(Mandatory = $true)][string]$Chars,
    [int]$Cell = 128,
    [int]$FontPx = 112,
    [float]$LineHeight = 0.9
)

Add-Type -AssemblyName System.Drawing

$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$pfc.AddFontFile($FontSource)
$family = $pfc.Families[0]

$list = @()
foreach ($ch in $Chars.ToCharArray()) {
    if ($list -notcontains $ch) { $list += $ch }
}

$count = $list.Count
$cols = [Math]::Ceiling([Math]::Sqrt($count))
$rows = [Math]::Ceiling($count / $cols)

$w = $cols * $Cell
$h = $rows * $Cell

$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$font = New-Object System.Drawing.Font($family, $FontPx, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
$brush = [System.Drawing.Brushes]::White

$jsonChars = @()
for ($i = 0; $i -lt $count; $i++) {
    $c = $list[$i]
    $col = $i % $cols
    $row = [Math]::Floor($i / $cols)
    $rect = New-Object System.Drawing.RectangleF(($col * $Cell), ($row * $Cell), $Cell, $Cell)
    $g.DrawString([string]$c, $font, $brush, $rect, $fmt)
    $jsonChars += ('        {{ "Character" : {0}, "Index" : {1} }}' -f $c, $i)
}

$g.Dispose()

$png = "$OutBase.png"
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$lines = @()
$lines += '{'
$lines += ('    "X" : {0},' -f $cols)
$lines += ('    "Y" : {0},' -f $rows)
$lines += ('    "DefaultHeight" : {0},' -f $LineHeight)
$lines += '    "Characters" : ['
$lines += ($jsonChars -join ",`r`n")
$lines += '    ]'
$lines += '}'

$json = "$OutBase.json"
[System.IO.File]::WriteAllText($json, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Output ("family=" + $family.Name + " chars=" + $count + " grid=" + $cols + "x" + $rows + " png=" + $w + "x" + $h)
Write-Output ("png  -> " + $png)
Write-Output ("json -> " + $json)
