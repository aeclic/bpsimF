
$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Drawing
$sig = @'
using System;
using System.Runtime.InteropServices;
public class Win2 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type -TypeDefinition $sig
$exe='D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Builds\HavokFinal\bpsim.exe'
$out='D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Tools\_final2'
$sw=[System.Diagnostics.Stopwatch]::StartNew()
$p=Start-Process -FilePath $exe -ArgumentList '-screen-width','1280','-screen-height','720','-screen-fullscreen','0' -PassThru
$h=[IntPtr]::Zero
for($i=0;$i -lt 120;$i++){
  Start-Sleep -Milliseconds 100
  $p.Refresh()
  if($p.MainWindowHandle -ne [IntPtr]::Zero -and [Win2]::IsWindowVisible($p.MainWindowHandle)){ $h=$p.MainWindowHandle; break }
}
Write-Output ("hwnd after " + [int]$sw.Elapsed.TotalMilliseconds + "ms")
$marks=@(5000,5800,6400,7000,7400,7800,8200,8600,9000,9400,9800,10400,11000)
foreach($m in $marks){
  $delay=$m-$sw.Elapsed.TotalMilliseconds
  if($delay -gt 0){ Start-Sleep -Milliseconds ([int]$delay) }
  $r=New-Object Win2+RECT
  [Win2]::GetWindowRect($h,[ref]$r) | Out-Null
  $w=$r.Right-$r.Left; $ht=$r.Bottom-$r.Top
  if($w -le 0){ $w=1280; $ht=720 }
  $bmp=New-Object System.Drawing.Bitmap $w,$ht
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $hdc=$g.GetHdc(); [Win2]::PrintWindow($h,$hdc,2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
  $f=Join-Path $out ("sq_" + $m + ".png")
  $bmp.Save($f,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Output ("t=" + $m + "ms -> " + $f)
}
$plog='C:\Users\aecli\AppData\LocalLow\BPRC\bpsimF\Player.log'
if(Test-Path $plog){ Copy-Item $plog 'D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Tools\_final2\Player.log' -Force }
try{ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }catch{}
