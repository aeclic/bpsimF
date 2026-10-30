Add-Type @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
}
"@
[W]::SetProcessDPIAware() | Out-Null
$out = "Tools/_final4"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$h = [IntPtr]::Zero
while ($sw.ElapsedMilliseconds -lt 15000) {
  $p = Get-Process bpsim -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if ($p) { $h = $p.MainWindowHandle; break }
  Start-Sleep -Milliseconds 100
}
if ($h -eq [IntPtr]::Zero) { Write-Output "no window"; exit 1 }
$cr = New-Object W+RECT; [W]::GetClientRect($h, [ref]$cr) | Out-Null
$wr = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$wr) | Out-Null
$pt = New-Object W+POINT; $pt.X = 0; $pt.Y = 0; [W]::ClientToScreen($h, [ref]$pt) | Out-Null
Write-Output ("client=" + $cr.R + "x" + $cr.B + " window=" + ($wr.R-$wr.L) + "x" + ($wr.B-$wr.T) + " at " + $pt.X + "," + $pt.Y + " after " + $sw.ElapsedMilliseconds + "ms")
$marks = @(0,600,1200,1800,2400,3000,3600,4200,4800,5400,6000,6600,7200,7800,8400,9000,10000,11000,12000,13000)
foreach ($m in $marks) {
  $wait = $m - $sw.ElapsedMilliseconds
  if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
  $bmp = New-Object System.Drawing.Bitmap(($wr.R-$wr.L), ($wr.B-$wr.T))
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [W]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save((Join-Path $out ("g" + $m + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}
Copy-Item "C:/Users/aecli/AppData/LocalLow/BPRC/bpsimF/Player.log" (Join-Path $out "Player.log") -Force -ErrorAction SilentlyContinue
Get-Process bpsim -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output "done"