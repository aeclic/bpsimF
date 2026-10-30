
$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Drawing
$sig = @'
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type -TypeDefinition $sig
$exe='D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Builds\HavokFinal\bpsim.exe'
$out='D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Tools\_final'
$p=Start-Process -FilePath $exe -ArgumentList '-screen-width','1280','-screen-height','720','-screen-fullscreen','0' -PassThru
$h=[IntPtr]::Zero
for($i=0;$i -lt 100;$i++){
  Start-Sleep -Milliseconds 100
  $p.Refresh()
  if($p.MainWindowHandle -ne [IntPtr]::Zero -and [Win]::IsWindowVisible($p.MainWindowHandle)){ $h=$p.MainWindowHandle; break }
}
Write-Output ("hwnd=" + $h + " after boot")
if($h -ne [IntPtr]::Zero){
  foreach($t in @(0.4,1.4,2.4)){
    Start-Sleep -Milliseconds ([int]($t*1000))
    $r=New-Object Win+RECT
    [Win]::GetWindowRect($h,[ref]$r) | Out-Null
    $w=$r.Right-$r.Left; $ht=$r.Bottom-$r.Top
    if($w -le 0){ $w=1280; $ht=720 }
    $bmp=New-Object System.Drawing.Bitmap $w,$ht
    $g=[System.Drawing.Graphics]::FromImage($bmp)
    $hdc=$g.GetHdc()
    [Win]::PrintWindow($h,$hdc,2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $f=Join-Path $out ("pw_" + [int]($t*10) + ".png")
    $bmp.Save($f,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Output ("captured " + $t + "s -> " + $f + " size " + $w + "x" + $ht)
  }
}
Start-Sleep -Milliseconds 300
try{ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }catch{}
