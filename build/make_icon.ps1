Add-Type -AssemblyName System.Drawing
$out = 'C:\Users\11600\Desktop\code\pomodoro-todo\build'
$sizes = 16,32,48,256
foreach($sz in $sizes){
  $bmp = New-Object System.Drawing.Bitmap -ArgumentList $sz,$sz
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $u = $sz/32.0
  function P($a){ [single]($a*$u) }

  # body: tomato red ellipse
  $rect = New-Object System.Drawing.RectangleF -ArgumentList (P 2.5),(P 8.5),(P 27),(P 22)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $rect, ([System.Drawing.Color]::FromArgb(255,255,107,94)), ([System.Drawing.Color]::FromArgb(255,214,58,44)), 90
  $g.FillEllipse($brush, $rect)
  # subtle highlight
  $hi = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70,255,255,255))
  $hr = New-Object System.Drawing.RectangleF -ArgumentList (P 6),(P 11),(P 7),(P 7)
  $g.FillEllipse($hi, $hr)
  # leaves: green star
  $leafDark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,38,166,90))
  $leafLit  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,61,220,132))
  $cx = P 16
  # left leaf
  $g.FillPolygon($leafDark, @([System.Drawing.PointF]::new($cx,(P 9.5)),[System.Drawing.PointF]::new((P 5),(P 5)),[System.Drawing.PointF]::new((P 10),(P 11))))
  # right leaf
  $g.FillPolygon($leafLit, @([System.Drawing.PointF]::new($cx,(P 9.5)),[System.Drawing.PointF]::new((P 27),(P 5)),[System.Drawing.PointF]::new((P 22),(P 11))))
  # center small leaf
  $g.FillPolygon($leafLit, @([System.Drawing.PointF]::new((P 14),(P 9)),[System.Drawing.PointF]::new((P 18),(P 9)),[System.Drawing.PointF]::new($cx,(P 3.5))))
  # stem
  $stemW = [Math]::Max(1.0,(P 1.6))
  $g.FillRectangle($leafDark, $cx-$stemW/2, (P 2), $stemW, (P 6))
  $g.Dispose()
  $bmp.Save((Join-Path $out ("icon$sz.png")), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}
Write-Host 'pngs done'
