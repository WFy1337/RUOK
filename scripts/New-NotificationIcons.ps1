param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '..\src\RUOK.App\Assets\NotificationFaces'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($score in 1..5) {
    foreach ($scale in @(100, 200, 400)) {
        foreach ($contrast in @('', '_contrast-black', '_contrast-white')) {
            $size = [int](16 * $scale / 100)
            $bitmap = [System.Drawing.Bitmap]::new($size, $size)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $color = if ($contrast -eq '_contrast-white') { [System.Drawing.Color]::Black } else { [System.Drawing.Color]::White }
            $pen = [System.Drawing.Pen]::new($color, 1.05)
            $brush = [System.Drawing.SolidBrush]::new($color)
            try {
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.ScaleTransform($size / 16.0, $size / 16.0)
                $graphics.DrawEllipse($pen, 1.0, 1.0, 14.0, 14.0)
                $graphics.FillEllipse($brush, 4.3, 4.9, 1.3, 1.3)
                $graphics.FillEllipse($brush, 10.4, 4.9, 1.3, 1.3)
                $controlY = @(5.0, 7.8, 10.1, 13.0, 15.5)[$score - 1]
                $curveY = [single](10.1 + (2.0 / 3.0) * ($controlY - 10.1))
                $graphics.DrawBezier($pen, 4.4, 10.1, 6.8, $curveY, 9.2, $curveY, 11.6, 10.1)
                $name = "Mood$score.scale-$scale$contrast.png"
                $bitmap.Save((Join-Path $destination $name), [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $brush.Dispose()
                $pen.Dispose()
                $graphics.Dispose()
                $bitmap.Dispose()
            }
        }
    }
}
Write-Output 'Generated 45 original face assets (5 faces, 3 scales, 3 contrast variants).'
