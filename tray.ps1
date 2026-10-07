# Pomodoro Todo - tray host (zero install, Windows built-in PowerShell + .NET)
$ErrorActionPreference = 'Stop'

# single instance
$mutex = New-Object System.Threading.Mutex($false, 'Global\PomodoroTodoTrayApp_v1')
if (-not $mutex.WaitOne(0)) { return }

$cs = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Win32 {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

  public static IntPtr FindWindow(uint[] pids, string match, out string title) {
    var set = new System.Collections.Generic.HashSet<uint>(pids);
    IntPtr found = IntPtr.Zero; string t = "";
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (set.Contains(pid)) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        var s = sb.ToString();
        if (s.Length > 0 && s.Contains(match)) { found = h; t = s; return false; }
      }
      return true;
    }, IntPtr.Zero);
    title = t;
    return found;
  }

  public static void ForceForeground(IntPtr hWnd) {
    ShowWindow(hWnd, 9);
    IntPtr fore = GetForegroundWindow();
    uint dummy;
    uint foreThread = GetWindowThreadProcessId(fore, out dummy);
    uint thisThread = GetCurrentThreadId();
    if (foreThread != thisThread) {
      AttachThreadInput(thisThread, foreThread, true);
      SetForegroundWindow(hWnd);
      AttachThreadInput(thisThread, foreThread, false);
    } else {
      SetForegroundWindow(hWnd);
    }
  }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$html = Join-Path $root 'index.html'
$url  = 'file:///' + ($html -replace '\\','/')

$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
$args = "--app=`"$url`" --window-size=980,760"
Start-Process -FilePath $edge -ArgumentList $args | Out-Null

# ---------- tomato icon ----------
$bmp = New-Object System.Drawing.Bitmap 32,32
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$red  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,229,72,72))
$g.FillEllipse($red, 3, 9, 26, 21)
$green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,61,220,120))
$g.FillPolygon($green, @([System.Drawing.Point]::new(16,2),[System.Drawing.Point]::new(6,11),[System.Drawing.Point]::new(16,9)))
$g.FillPolygon($green, @([System.Drawing.Point]::new(16,2),[System.Drawing.Point]::new(26,11),[System.Drawing.Point]::new(16,9)))
$stem = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,40,150,80))
$g.FillRectangle($stem, 15, 3, 2, 5)
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())

# ---------- tray icon ----------
$notify = New-Object System.Windows.Forms.NotifyIcon
$notify.Icon = $icon
$notify.Text = '番茄待办 - 双击显示/隐藏窗口'
$notify.Visible = $true
$menu = New-Object System.Windows.Forms.ContextMenuStrip
[void]$menu.Items.Add('显示窗口')
[void]$menu.Items.Add('隐藏窗口')
[void]$menu.Items.Add('-')
[void]$menu.Items.Add('退出番茄待办')
$notify.ContextMenuStrip = $menu
$notify.ShowBalloonTip(4000, '番茄待办', '已驻留系统托盘：最小化自动收起，双击图标显示/隐藏', [System.Windows.Forms.ToolTipIcon]::Info)

# ---------- state ----------
$script:hwnd = [IntPtr]::Zero
$script:missing = 0
$script:alarmHandled = $false
$ALARM_MARK = [char]0x3010 + [string][char]0x63D0 + [char]0x9192 + [char]0x3011  # 【提醒】
$TITLE_MATCH = [char]0x756A + [char]0x8304 + [char]0x5F85 + [char]0x529E          # 番茄待办

function Show-Window {
  if ($script:hwnd -eq [IntPtr]::Zero) { return }
  [Win32]::ForceForeground($script:hwnd) | Out-Null
}
function Hide-Window {
  if ($script:hwnd -eq [IntPtr]::Zero) { return }
  [Win32]::ShowWindow($script:hwnd, 0) | Out-Null
}
function Toggle-Window {
  if ($script:hwnd -eq [IntPtr]::Zero) {
    $notify.ShowBalloonTip(2000, '番茄待办', '窗口尚未就绪，请稍候', [System.Windows.Forms.ToolTipIcon]::Info)
    return
  }
  if ([Win32]::IsIconic($script:hwnd) -or -not [Win32]::IsWindowVisible($script:hwnd)) { Show-Window }
  else { Hide-Window }
}
function Stop-App {
  $timer.Stop()
  if ($script:hwnd -ne [IntPtr]::Zero) {
    [Win32]::PostMessage($script:hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  }
  Start-Sleep -Milliseconds 300
  $notify.Visible = $false
  $notify.Dispose()
  $context.ExitThread()
  $mutex.ReleaseMutex() | Out-Null
  $mutex.Dispose()
}

# ---------- events ----------
$notify.add_MouseDoubleClick({ param($s,$e)
  if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) { Toggle-Window }
})
$menu.Items[0].add_Click({ Show-Window })
$menu.Items[1].add_Click({ Hide-Window })
$menu.Items[3].add_Click({ Stop-App })

# ---------- timer ----------
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 800
$timer.Add_Tick({
  try {
    $pids = @(Get-Process msedge -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
    if ($pids.Count -eq 0) { $pids = @([uint32]0) }
    $title = ''
    $h = [Win32]::FindWindow($pids, $TITLE_MATCH, [ref]$title)
    if ($h -ne [IntPtr]::Zero) {
      $isFirst = ($script:hwnd -eq [IntPtr]::Zero)
      $script:hwnd = $h
      $script:missing = 0
      # cold start: force the window visible & foreground
      if ($isFirst) {
        [Win32]::ForceForeground($h) | Out-Null
      }
      # alarm: force popup
      if ($title.StartsWith($ALARM_MARK)) {
        if (-not $script:alarmHandled) {
          $script:alarmHandled = $true
          [Win32]::ForceForeground($h) | Out-Null
          $notify.ShowBalloonTip(3000, '番茄待办', $title, [System.Windows.Forms.ToolTipIcon]::Warning)
        }
      } else {
        $script:alarmHandled = $false
      }
      # minimize stays on the taskbar; hiding to tray is manual via the tray menu only
    } else {
      $script:missing++
      if ($script:missing -gt 20) { Stop-App }
    }
  } catch { }
})
$timer.Start()

# ---------- message loop ----------
$context = New-Object System.Windows.Forms.ApplicationContext
[System.Windows.Forms.Application]::Run($context)
