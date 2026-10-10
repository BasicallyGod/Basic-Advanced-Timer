param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

# Placeholder artwork for local packaging checks; replace before a Store submission.
foreach ($item in @(
    @{ Name = 'Square44x44Logo.png'; Size = 44 },
    @{ Name = 'Square150x150Logo.png'; Size = 150 },
    @{ Name = 'StoreLogo.png'; Size = 50 }
)) {
    $size = [int]$item.Size
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $face = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(51, 122, 80))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [Math]::Max(2, $size * 0.067))
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $margin = [int][Math]::Round($size * 0.07)
        $graphics.FillEllipse($face, $margin, $margin, $size - (2 * $margin), $size - (2 * $margin))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $center = [int]($size / 2)
        $graphics.DrawLine($pen, $center, $center, [int]($size * 0.27))
        $graphics.DrawLine($pen, $center, $center, [int]($size * 0.69), [int]($size * 0.60))
        $bitmap.Save((Join-Path $Destination $item.Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $pen.Dispose()
        $face.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
