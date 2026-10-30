
$ErrorActionPreference = 'Continue'
$exe = 'D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Builds\HavokFinal\bpsim.exe'
$out = 'D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Tools\_final'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$p = Start-Process -FilePath $exe -ArgumentList '-screen-width','1280','-screen-height','720','-screen-fullscreen','0' -PassThru
$shots = @(1.0, 2.6, 5.0)
$prev = 0
foreach ($t in $shots) {
  $wait = $t - $prev
  if ($wait -gt 0) { Start-Sleep -Milliseconds ([int]($wait*1000)) }
  $prev = $t
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
  $f = Join-Path $out ("player_t" + [int]($t*10) + ".png")
  $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ("shot " + $t + " -> " + $f)
}
Start-Sleep -Milliseconds 500
try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch {}
Write-Output ("player exited=" + $p.HasExited)
