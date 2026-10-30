$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Win3 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$out='D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Tools\_final3'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$sw=[System.Diagnostics.Stopwatch]::StartNew()
$h=[IntPtr]::Zero
for($i=0;$i -lt 200;$i++){
  $proc=Get-Process bpsim -ErrorAction SilentlyContinue | Select-Object -First 1
  if($proc){
    $proc.Refresh()
    if($proc.MainWindowHandle -ne [IntPtr]::Zero -and [Win3]::IsWindowVisible($proc.MainWindowHandle)){ $h=$proc.MainWindowHandle; break }
  }
  Start-Sleep -Milliseconds 100
}
Write-Output ("hwnd after " + [int]$sw.Elapsed.TotalMilliseconds + "ms h=" + [int64]$h)
if($h -ne [IntPtr]::Zero){
  $marks=@(0,700,1400,2100,2800,3500,4200,4900,5600,6300,7000,7700,8400,9100,9800,10500,11200,11900,12600)
  foreach($m in $marks){
    $delay=$m-$sw.Elapsed.TotalMilliseconds
    if($delay -gt 0){ Start-Sleep -Milliseconds ([int]$delay) }
    $r=New-Object Win3+RECT
    [Win3]::GetWindowRect($h,[ref]$r) | Out-Null
    $w=$r.Right-$r.Left; $ht=$r.Bottom-$r.Top
    if($w -le 0){ $w=1280; $ht=720 }
    $bmp=New-Object System.Drawing.Bitmap $w,$ht
    $g=[System.Drawing.Graphics]::FromImage($bmp)
    $hdc=$g.GetHdc(); [Win3]::PrintWindow($h,$hdc,2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $f=Join-Path $out ("g_" + $m + ".png")
    $bmp.Save($f,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Output ("t=" + $m + "ms -> g_" + $m + ".png")
  }
}
$plog='C:\Users\aecli\AppData\LocalLow\BPRC\bpsimF\Player.log'
if(Test-Path $plog){ Copy-Item $plog (Join-Path $out 'Player.log') -Force }
try{ Get-Process bpsim -EA SilentlyContinue | Stop-Process -Force }catch{}
