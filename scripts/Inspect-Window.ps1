param([int]$ProcessId, [string]$Output, [int]$Width=1180, [int]$Height=820, [string]$Invoke, [string]$Keys)
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public class CaptureNative {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int height,bool repaint);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
[CaptureNative]::SetProcessDPIAware() | Out-Null
$p = Get-Process -Id $ProcessId
$handle = $p.MainWindowHandle
[CaptureNative]::MoveWindow($handle, 30,30,$Width,$Height,$true) | Out-Null
[CaptureNative]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 400
$root = [Windows.Automation.AutomationElement]::FromHandle($handle)
if ($Invoke) {
 $element = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Invoke))
 if (!$element) { throw "Not found: $Invoke" }
 $pattern=$null
 if($element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){ $pattern.Invoke() }
 elseif($element.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern,[ref]$pattern)){ $pattern.Toggle() }
 else { throw "No invokable pattern: $Invoke" }
 Start-Sleep -Milliseconds 500
}
if ($Keys) { [Windows.Forms.SendKeys]::SendWait($Keys); Start-Sleep -Milliseconds 600 }
$all = $root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
$lines = foreach ($element in $all) {
 try { $c=$element.Current; if($c.Name) { '{0} | {1} | {2} | enabled={3} offscreen={4}' -f $c.ControlType.ProgrammaticName,$c.AutomationId,$c.Name,$c.IsEnabled,$c.IsOffscreen } } catch {}
}
$lines
if ($Output) {
 $lines | Out-File ($Output + '.txt') -Encoding utf8
 $rect = New-Object CaptureNative+RECT
 [CaptureNative]::GetWindowRect($handle,[ref]$rect) | Out-Null
 $bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
 $g=[Drawing.Graphics]::FromImage($bitmap)
 $dc=$g.GetHdc()
 [CaptureNative]::PrintWindow($handle,$dc,2) | Out-Null
 $g.ReleaseHdc($dc)
 $bitmap.Save($Output,[Drawing.Imaging.ImageFormat]::Png)
 $g.Dispose();$bitmap.Dispose()
}
