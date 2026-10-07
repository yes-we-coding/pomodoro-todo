using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PomodoroTodo
{
    static class Win32
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int Left, Right, Top, Bottom; }
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;

        public static IntPtr FindWindow(HashSet<uint> pids, string match, out string title)
        {
            IntPtr found = IntPtr.Zero;
            string t = "";
            EnumWindows((h, l) =>
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pids.Contains(pid))
                {
                    var sb = new StringBuilder(512);
                    GetWindowText(h, sb, 512);
                    var s = sb.ToString();
                    if (s.Length > 0 && s.Contains(match))
                    {
                        found = h;
                        t = s;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            title = t;
            return found;
        }

        public static void ForceForeground(IntPtr hWnd)
        {
            ShowWindow(hWnd, 9); // SW_RESTORE
            IntPtr fore = GetForegroundWindow();
            uint dummy;
            uint foreThread = GetWindowThreadProcessId(fore, out dummy);
            uint thisThread = GetCurrentThreadId();
            if (foreThread != thisThread)
            {
                AttachThreadInput(thisThread, foreThread, true);
                SetForegroundWindow(hWnd);
                AttachThreadInput(thisThread, foreThread, false);
            }
            else
            {
                SetForegroundWindow(hWnd);
            }
        }
    }

    // 计时器会话状态（与 JS 约定）
    class TimerState
    {
        public string Raw = "";
        public bool Running;
        public string Mode = "";
        public long EndAt;
        public int Remaining = -1;
    }

    static class Program
    {
        const string TITLE_MATCH = "番茄待办";
        const string ALARM_MARK = "【提醒】";

        static Mutex mutex;
        static NotifyIcon notify;
        static ContextMenuStrip menu;
        static System.Windows.Forms.Timer timer;
        static IntPtr hwnd = IntPtr.Zero;
        static IntPtr lastHwnd = IntPtr.Zero;
        static IntPtr styledHwnd = IntPtr.Zero;
        static bool alarmHandled = false;
        static Icon appIcon;
        static string dataDir;
        static string sessionFile;
        static string dataFile;
        static string backupDir;
        static string snapshotDir;      // A: 滚动快照目录
        static int lastSnapshot = 0;    // A: 上次快照的 tick
        static string winFile;          // 窗口尺寸/位置记忆
        static DateTime lastReopen = DateTime.MinValue;
        static bool armed = false;   // 计时进行中关窗后，等待到点重开
        static bool prevWindowFound = false;
        static int lastGeomSave = 0;

        static HttpListener http;
        static int httpPort;
        static string token;
        static readonly object stateLock = new object();
        static TimerState ts = new TimerState();

        // 窗口形态
        static bool isPinned = false;
        static bool isMini = false;



        // 久坐提醒
        static bool sitEnabled = false;
        static int sitEveryMin = 45;            // 连续多少分钟提醒
        static DateTime sitStart = DateTime.Now; // 本轮连续在电脑前的起点
        static bool sitNotified = false;
        const int IDLE_RESET_MS = 180000;       // 离开 3 分钟视为已休息，自动重置

        const string MUTEX_NAME = "Global\\PomodoroTodoApp_v2";

        [STAThread]
        static void Main()
        {
            try { RunApp(); }
            catch (Exception ex)
            {
                try
                {
                    string log = Path.Combine(Path.GetTempPath(), "pt_crash.log");
                    File.WriteAllText(log, ex.ToString(), Encoding.UTF8);
                    MessageBox.Show(ex.ToString(), "番茄待办启动错误");
                }
                catch { }
            }
        }
        static void RunApp()
        {
            bool created;
            mutex = new Mutex(true, MUTEX_NAME, out created);
            if (!created)
            {
                // 可能是真正的第二实例，也可能是残留/遗弃句柄。
                try
                {
                    bool got = mutex.WaitOne(800);
                    if (got) { /* 成功接管 */ }
                    else { ActivateExistingInstance(); return; }
                }
                catch (AbandonedMutexException)
                {
                    // 前一持有者异常退出，锁已归我，安全接管
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1) prepare data dir and extract embedded app files
            dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PomodoroTodo");
            Directory.CreateDirectory(dataDir);
            ExtractResource("index.html", Path.Combine(dataDir, "index.html"));
            ExtractResource("favicon.ico", Path.Combine(dataDir, "favicon.ico"));
            Directory.CreateDirectory(Path.Combine(dataDir, "build"));
            ExtractResource("icon256.png", Path.Combine(dataDir, "build", "icon256.png"));

            sessionFile = Path.Combine(dataDir, "session.json");
            dataFile = Path.Combine(dataDir, "data.json");
            backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "番茄待办备份");
            winFile = Path.Combine(dataDir, "window.json");
            snapshotDir = Path.Combine(backupDir, "snapshots");

            // 2) load icon
            using (var icoStream = ResourceStream("app.ico"))
                appIcon = new Icon(icoStream, SystemInformation.SmallIconSize);

            // 3) tray icon
            notify = new NotifyIcon();
            notify.Icon = appIcon;
            notify.Text = "番茄待办 - 双击显示/隐藏窗口";
            notify.Visible = true;

            menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口");
            menu.Items.Add("隐藏窗口");
            menu.Items.Add("🚶 我已起身活动");
            menu.Items.Add("-");
            menu.Items.Add("退出番茄待办");
            notify.ContextMenuStrip = menu;
            ((ToolStripMenuItem)menu.Items[0]).Click += (s, e) => ShowWindow();
            ((ToolStripMenuItem)menu.Items[1]).Click += (s, e) => HideWindow();
            ((ToolStripMenuItem)menu.Items[2]).Click += (s, e) => ResetSit();
            ((ToolStripMenuItem)menu.Items[4]).Click += (s, e) => StopApp();
            notify.BalloonTipClicked += (s, e) => { if(sitNotified) ResetSit(); };
            notify.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ToggleWindow();
            };
            notify.ShowBalloonTip(4000, "番茄待办",
                "已驻留系统托盘：关闭窗口计时不中断，到点自动提醒；数据本地保存，每日自动备份",
                ToolTipIcon.Info);

            // 4) load previous session, start local helper HTTP service
            LoadSessionFromDisk();
            token = Guid.NewGuid().ToString("N");
            httpPort = StartHttp();

            // 5) write config.js so the page knows port/token, then launch window
            WriteConfigJs();
            LaunchEdge();

            // 6) polling
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 800;
            timer.Tick += Tick;
            timer.Start();

            Application.Run();
        }

        static void LaunchEdge()
        {
            string edge = FindEdge();
            if (edge == null)
            {
                MessageBox.Show("未找到 Microsoft Edge，请先安装 Edge 浏览器。", "番茄待办",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string url = "file:///" + Path.Combine(dataDir, "index.html").Replace('\\', '/');
            string size = isMini ? "360,520" : SavedWinSize();
            Process.Start(new ProcessStartInfo
            {
                FileName = edge,
                Arguments = "--app=\"" + url + "\" --window-size=" + size,
                UseShellExecute = false
            });
        }

        // ---------------- 窗口尺寸/位置记忆 ----------------
        static string SavedWinSize()
        {
            try
            {
                if (File.Exists(winFile))
                {
                    string s = File.ReadAllText(winFile, Encoding.UTF8);
                    var mW = Regex.Match(s, "\\\"w\\\"\\s*:\\s*(\\d+)");
                    var mH = Regex.Match(s, "\\\"h\\\"\\s*:\\s*(\\d+)");
                    if (mW.Success && mH.Success)
                    {
                        int w = int.Parse(mW.Groups[1].Value);
                        int h = int.Parse(mH.Groups[1].Value);
                        if (w >= 320 && w <= 6000 && h >= 320 && h <= 6000)
                            return w + "," + h;
                    }
                }
            }
            catch { }
            return "980,760";
        }

        static void SaveWinGeometry(IntPtr h)
        {
            try
            {
                Win32.RECT r;
                if (!Win32.GetWindowRect(h, out r)) return;
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                if (w < 200 || hh < 200) return;      // 迷你/异常尺寸不记录
                int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
                if (isMini || (w >= sw - 40 && hh >= sh - 40)) return;  // 最大化不记录
                string json = "{\"w\":" + w + ",\"h\":" + hh + ",\"x\":" + r.Left + ",\"y\":" + r.Top + "}";
                File.WriteAllText(winFile, json, new UTF8Encoding(false));
            }
            catch { }
        }

        static void Tick(object sender, EventArgs e)
        {
            try
            {
                var pids = new HashSet<uint>();
                foreach (var p in Process.GetProcessesByName("msedge"))
                    pids.Add((uint)p.Id);

                string title;
                IntPtr h = Win32.FindWindow(pids, TITLE_MATCH, out title);
                bool found = (h != IntPtr.Zero);
                if (found)
                {
                    bool first = (styledHwnd == IntPtr.Zero || styledHwnd != h);
                    hwnd = h; lastHwnd = h;
                    ApplyFluentChrome(h);
                    // C: 节流保存窗口尺寸/位置（每~2秒）
                    if (Environment.TickCount - lastGeomSave > 2000)
                    {
                        lastGeomSave = Environment.TickCount;
                        SaveWinGeometry(h);
                    }
                    if (first)
                    {
                        Win32.ForceForeground(h);
                        if (isMini || isPinned) ApplyWindowShape(h);
                    }

                    if (title.StartsWith(ALARM_MARK))
                    {
                        if (!alarmHandled)
                        {
                            alarmHandled = true;
                            Win32.ForceForeground(h);
                            notify.ShowBalloonTip(3000, "番茄待办", title, ToolTipIcon.Warning);
                        }
                    }
                    else alarmHandled = false;
                }
                else
                {
                    // 窗口刚消失：先记住尺寸/位置，再若计时仍在进行则武装后台计时
                    if (prevWindowFound)
                    {
                        if (lastHwnd != IntPtr.Zero) SaveWinGeometry(lastHwnd);
                        lock (stateLock)
                        {
                            long nowMs2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            if (ts.Running && ts.EndAt > nowMs2)
                            {
                                armed = true;
                                // D: 关窗但计时仍在进行 -> 气泡告知
                                if (notify != null)
                                {
                                    int remainMin = (int)Math.Max(1, (ts.EndAt - nowMs2) / 60000);
                                    notify.ShowBalloonTip(3500, "番茄待办 · 计时继续",
                                        "窗口已关闭，但计时仍在后台进行（剩约 " + remainMin + " 分钟）。双击托盘图标可随时查看。",
                                        ToolTipIcon.Info);
                                }
                            }
                            else if (notify != null)
                            {
                                notify.ShowBalloonTip(2500, "番茄待办",
                                    "窗口已关闭；双击托盘图标可重新打开。", ToolTipIcon.Info);
                            }
                        }
                    }
                    if (hwnd != IntPtr.Zero) lastHwnd = hwnd;
                    hwnd = IntPtr.Zero;
                    // 计时在关窗期间到点：只重开一次
                    if (armed && SessionDue())
                    {
                        armed = false;
                        lastReopen = DateTime.Now;
                        LaunchEdge();
                    }
                }
                prevWindowFound = found;
            }
            catch { }
            CheckSit();
        }

        // ---------------- 久坐提醒 ----------------
        static int IdleMs()
        {
            try
            {
                var lii = new Win32.LASTINPUTINFO();
                lii.cbSize = (uint)Marshal.SizeOf(lii);
                if (Win32.GetLastInputInfo(ref lii))
                    return Environment.TickCount - (int)lii.dwTime;
            }
            catch { }
            return 0;
        }
        static void CheckSit()
        {
            if (!sitEnabled) return;
            int idle = IdleMs();
            if (idle >= IDLE_RESET_MS)
            {
                // 已离开一段时间，视为休息过
                sitStart = DateTime.Now;
                sitNotified = false;
                return;
            }
            if (!sitNotified && (DateTime.Now - sitStart).TotalSeconds >= sitEveryMin * 60)
            {
                sitNotified = true;
                notify.ShowBalloonTip(10000, "久坐提醒 🚶",
                    "已连续在电脑前约 " + sitEveryMin + " 分钟，起来走走、伸展一下、喝口水吧！（点此气泡标记已活动）",
                    ToolTipIcon.Warning);
            }
        }
        static void ResetSit()
        {
            sitStart = DateTime.Now;
            sitNotified = false;
        }

        static bool SessionDue()
        {
            lock (stateLock)
            {
                if (String.IsNullOrEmpty(ts.Mode)) return false;
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return ts.Running && ts.EndAt > 0 && nowMs >= ts.EndAt;
            }
        }

        static void ShowWindow()
        {
            if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd)) Win32.ForceForeground(hwnd);
            else LaunchEdge();
        }
        static void HideWindow()
        {
            if (hwnd != IntPtr.Zero) Win32.ShowWindow(hwnd, 0);
        }
        static void ToggleWindow()
        {
            if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd)
                && (Win32.IsIconic(hwnd) || !Win32.IsWindowVisible(hwnd))) ShowWindow();
            else if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd)) HideWindow();
            else LaunchEdge();
        }

        // ---------------- Fluent 无边框改造 ----------------
        const int GWL_STYLE = -16;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWCP_ROUND = 2;
        const long WS_MINIMIZEBOX_l = 0x00020000L;
        const long WS_MAXIMIZEBOX_l = 0x00010000L;
        const long WS_SYSMENU_l     = 0x00080000L;
        const long WS_CAPTION       = 0x00C00000L;
        const long WS_THICKFRAME_l  = 0x00040000L;

        static void ApplyFluentChrome(IntPtr h)
        {
            try
            {
                long s = Win32.GetWindowLongPtrW(h, GWL_STYLE).ToInt64();
                long wanted = s;
                wanted &= ~WS_CAPTION;
                wanted |= (WS_THICKFRAME_l | WS_SYSMENU_l | WS_MINIMIZEBOX_l | WS_MAXIMIZEBOX_l);
                if (wanted != s)
                {
                    Win32.SetWindowLongPtrW(h, GWL_STYLE, new IntPtr(wanted));
                    Win32.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                        Win32.SWP_NOMOVE | Win32.SWP_NOSIZE
                        | 0x0004 | 0x0020);
                }
                // DWM 帧扩展：尝试消除可调边框产生的可见帧
                var mg = new Win32.MARGINS();
                mg.Left = 1; mg.Right = 1; mg.Top = 1; mg.Bottom = 1;
                Win32.DwmExtendFrameIntoClientArea(h, ref mg);
                // 圆角（Win11）
                int corner = DWMWCP_ROUND;
                Win32.DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                styledHwnd = h;
            }
            catch { }
        }

        static void WinAction(string act)
        {
            const uint WM_NCLBUTTONDOWN = 0x00A1, WM_CLOSE = 0x0010;
            const int HTCAPTION = 2;
            IntPtr h = hwnd;
            if (h == IntPtr.Zero) return;
            if (act == "drag")
            {
                Win32.ReleaseCapture();
                Win32.SendMessage(h, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            }
            else if (act == "min")
                Win32.ShowWindow(h, 6); // SW_MINIMIZE
            else if (act == "show")
                Win32.ShowWindow(h, 9); // SW_RESTORE
            else if (act == "max")
            {
                if (Win32.IsZoomed(h)) Win32.ShowWindow(h, 9);
                else Win32.ShowWindow(h, 3); // SW_MAXIMIZE
            }
            else if (act == "close")
                Win32.PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        // ---------------- 窗口置顶 / 迷你 ----------------
        static void ApplyWindowShape(IntPtr h)
        {
            Win32.SetWindowPos(h, isPinned ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW);
            if (isMini)
            {
                Win32.RECT r;
                Win32.GetWindowRect(h, out r);
                int w = 360, hh = 520;
                // 保持右下角不跑出屏幕
                int sw = Win32.GetSystemMetrics(0);
                int x = r.Left; if (x + w > sw - 20) x = sw - w - 20;
                Win32.SetWindowPos(h, isPinned ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                    x, r.Top, w, hh, Win32.SWP_SHOWWINDOW);
            }
            else
            {
                Win32.RECT r;
                Win32.GetWindowRect(h, out r);
                Win32.SetWindowPos(h, isPinned ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                    r.Left, r.Top, 980, 760, Win32.SWP_SHOWWINDOW);
            }
        }

        static void StopApp()
        {
            if (timer != null) timer.Stop();
            try { if (http != null) http.Stop(); } catch { }
            if (hwnd != IntPtr.Zero)
                Win32.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(300);
            if (notify != null)
            {
                notify.Visible = false;
                notify.Dispose();
            }
            if (appIcon != null) appIcon.Dispose();
            if (mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
            Application.Exit();
        }

        static string FindEdge()
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates = {
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf,   @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        // ---------------- HTTP helper service ----------------
        static int StartHttp()
        {
            for (int port = 58213; port <= 58223; port++)
            {
                try
                {
                    var l = new HttpListener();
                    l.Prefixes.Add("http://localhost:" + port + "/");
                    l.Start();
                    http = l;
                    var t = new Thread(ServerLoop) { IsBackground = true };
                    t.Start();
                    return port;
                }
                catch { }
            }
            return 0;
        }

        static void ServerLoop()
        {
            while (http != null && http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = http.GetContext(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            try
            {
                AddCors(ctx);
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
                if (ctx.Request.Headers["X-PT-Token"] != token)
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    return;
                }

                if (path == "/state" && ctx.Request.HttpMethod == "GET")
                {
                    lock (stateLock) ReplyText(ctx, ts.Raw ?? "");
                }
                else if (path == "/state" && ctx.Request.HttpMethod == "POST")
                {
                    string body = ReadBody(ctx);
                    SaveSession(body);
                    ReplyText(ctx, "{\"ok\":true}");
                }
                else if (path == "/data" && ctx.Request.HttpMethod == "GET")
                {
                    string d = "";
                    try { if (File.Exists(dataFile)) d = File.ReadAllText(dataFile, Encoding.UTF8); } catch { }
                    ReplyText(ctx, d);
                }
                else if (path == "/data" && ctx.Request.HttpMethod == "POST")
                {
                    string body = ReadBody(ctx);
                    lock (stateLock)
                    {
                        try { File.WriteAllText(dataFile, body, new UTF8Encoding(false)); } catch { }
                    }
                    WriteSnapshot(body, false);   // A: 随手滚动快照
                    ReplyText(ctx, "{\"ok\":true}");
                }
                else if (path == "/snapshots" && ctx.Request.HttpMethod == "GET")
                {
                    // A: 列出可用快照（名称+时间），供前端“恢复”用
                    var sb = new StringBuilder();
                    sb.Append("[");
                    bool firstItem = true;
                    try
                    {
                        if (Directory.Exists(snapshotDir))
                        {
                            var files = Directory.GetFiles(snapshotDir, "snap_*.json");
                            Array.Sort(files);
                            foreach (var f in files)
                            {
                                var fi = new FileInfo(f);
                                if (!firstItem) sb.Append(",");
                                firstItem = false;
                                sb.Append("{\"name\":\"").Append(Path.GetFileName(f))
                                  .Append("\",\"time\":\"").Append(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))
                                  .Append("\",\"size\":").Append(fi.Length).Append("}");
                            }
                        }
                    }
                    catch { }
                    sb.Append("]");
                    ReplyText(ctx, sb.ToString());
                }
                else if (path == "/restore-snapshot" && ctx.Request.HttpMethod == "POST")
                {
                    // A: 用指定快照覆盖 data.json
                    string body = ReadBody(ctx);
                    var mm = Regex.Match(body, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    if (mm.Success)
                    {
                        string sn = Path.GetFileName(mm.Groups[1].Value);   // 防路径穿越
                        string sp = Path.Combine(snapshotDir, sn);
                        if (File.Exists(sp))
                        {
                            string content = File.ReadAllText(sp, Encoding.UTF8);
                            lock (stateLock)
                            {
                                try { File.WriteAllText(dataFile, content, new UTF8Encoding(false)); } catch { }
                            }
                            // 恢复前先留一份当前状态
                            try { File.WriteAllText(Path.Combine(snapshotDir, "snap_before_restore.json"), content, new UTF8Encoding(false)); } catch { }
                            ReplyText(ctx, "{\"ok\":true,\"data\":" + content + "}");
                        }
                        else ReplyText(ctx, "{\"ok\":false,\"err\":\"notfound\"}");
                    }
                    else ReplyText(ctx, "{\"ok\":false}");
                }
                else if (path == "/win" && ctx.Request.HttpMethod == "POST")
                {
                    string body = ReadBody(ctx);
                    var mAct = Regex.Match(body, "\"act\"\\s*:\\s*\"([^\"]+)\"");
                    if (mAct.Success)
                    {
                        string act = mAct.Groups[1].Value;
                        WinAction(act);
                        bool zoomed = (hwnd != IntPtr.Zero) && Win32.IsZoomed(hwnd);
                        ReplyText(ctx, "{\"ok\":true,\"zoomed\":" + (zoomed?"true":"false") + "}");
                        return;
                    }
                    var mPin = Regex.Match(body, "\"pin\"\\s*:\\s*(true|false)");
                    var mMini = Regex.Match(body, "\"mini\"\\s*:\\s*(true|false)");
                    if (mPin.Success) isPinned = mPin.Groups[1].Value == "true";
                    if (mMini.Success) isMini = mMini.Groups[1].Value == "true";
                    if (hwnd != IntPtr.Zero) ApplyWindowShape(hwnd);
                    ReplyText(ctx, "{\"ok\":true,\"pin\":" + (isPinned?"true":"false") + ",\"mini\":" + (isMini?"true":"false") + "}");
                }
                else if (path == "/win" && ctx.Request.HttpMethod == "GET")
                {
                    bool zoomed = (hwnd != IntPtr.Zero) && Win32.IsZoomed(hwnd);
                    ReplyText(ctx, "{\"pin\":" + (isPinned?"true":"false")
                        + ",\"mini\":" + (isMini?"true":"false")
                        + ",\"zoomed\":" + (zoomed?"true":"false") + "}");
                }
                else if (path == "/sit" && ctx.Request.HttpMethod == "GET")
                {
                    int remain = Math.Max(0, sitEveryMin*60 - (int)(DateTime.Now - sitStart).TotalSeconds);
                    ReplyText(ctx, "{\"enabled\":" + (sitEnabled?"true":"false")
                        + ",\"everyMin\":" + sitEveryMin
                        + ",\"remainSec\":" + remain + "}");
                }
                else if (path == "/sit" && ctx.Request.HttpMethod == "POST")
                {
                    string body = ReadBody(ctx);
                    var mE = Regex.Match(body, "\"enabled\"\\s*:\\s*(true|false)");
                    var mM = Regex.Match(body, "\"everyMin\"\\s*:\\s*(\\d+)");
                    if (mE.Success) sitEnabled = mE.Groups[1].Value == "true";
                    if (mM.Success) sitEveryMin = Math.Max(5, Math.Min(240, Int32.Parse(mM.Groups[1].Value)));
                    ResetSit();
                    ReplyText(ctx, "{\"ok\":true}");
                }
                else if (path == "/backup-status" && ctx.Request.HttpMethod == "GET")
                {
                    ReplyText(ctx, "{\"today\":" + (TodayBackupExists() ? "true" : "false") + "}");
                }
                else if (path == "/backup" && ctx.Request.HttpMethod == "POST")
                {
                    WriteBackup(ReadBody(ctx));
                    ReplyText(ctx, "{\"ok\":true}");
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch { try { ctx.Response.Close(); } catch { } }
        }

        static void AddCors(HttpListenerContext ctx)
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "X-PT-Token, Content-Type";
        }

        static string ReadBody(HttpListenerContext ctx)
        {
            // fetch 发送的 body 始终是 UTF-8，强制按 UTF-8 解码
            using (var sr = new StreamReader(ctx.Request.InputStream, new UTF8Encoding(false)))
            {
                string s = sr.ReadToEnd();
                return s.Length > 8000000 ? s.Substring(0, 8000000) : s;
            }
        }
        static void ReplyText(HttpListenerContext ctx, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = b.Length;
            ctx.Response.OutputStream.Write(b, 0, b.Length);
            ctx.Response.OutputStream.Close();
        }

        // ---------------- session persistence ----------------
        static void LoadSessionFromDisk()
        {
            try
            {
                if (File.Exists(sessionFile))
                    ParseSession(File.ReadAllText(sessionFile, Encoding.UTF8));
            }
            catch { }
        }
        static void SaveSession(string raw)
        {
            lock (stateLock)
            {
                ParseSession(raw);
                // 页面更新了状态（弹窗处理/暂停/跳过），取消后台武装
                armed = false;
                try { File.WriteAllText(sessionFile, raw, new UTF8Encoding(false)); } catch { }
            }
        }
        static void ParseSession(string raw)
        {
            var s = new TimerState { Raw = raw };
            var m = Regex.Match(raw, "\"running\"\\s*:\\s*(true|false)");
            s.Running = m.Success && m.Groups[1].Value == "true";
            m = Regex.Match(raw, "\"mode\"\\s*:\\s*\"([^\"]+)\"");
            if (m.Success) s.Mode = m.Groups[1].Value;
            m = Regex.Match(raw, "\"endAt\"\\s*:\\s*(\\d+)");
            if (m.Success) s.EndAt = Int64.Parse(m.Groups[1].Value);
            m = Regex.Match(raw, "\"remaining\"\\s*:\\s*(\\d+)");
            if (m.Success) s.Remaining = Int32.Parse(m.Groups[1].Value);
            ts = s;
        }

        // ---------------- automatic backups ----------------
        static string DayKey(DateTime d)
        {
            return d.Year + "-" + d.Month.ToString("00") + "-" + d.Day.ToString("00");
        }

        static bool TodayBackupExists()
        {
            return File.Exists(Path.Combine(backupDir, "番茄待办_" + DayKey(DateTime.Now) + ".json"));
        }

        static void WriteBackup(string body)
        {
            Directory.CreateDirectory(backupDir);
            // 1) 每日一份（首次写入）
            string file = Path.Combine(backupDir, "番茄待办_" + DayKey(DateTime.Now) + ".json");
            if (!File.Exists(file)) File.WriteAllText(file, body, new UTF8Encoding(false));
            PruneBackups();
        }

        // A: 滚动快照（带时间戳，保留最近 N 份。供“数据丢失时找回来”）
        static void WriteSnapshot(string body, bool force)
        {
            try
            {
                if (!force && Environment.TickCount - lastSnapshot < 10 * 60 * 1000) return; // 节流：每10分钟
                Directory.CreateDirectory(snapshotDir);
                var now = DateTime.Now;
                string stamp = now.Year + now.Month.ToString("00") + now.Day.ToString("00")
                    + "_" + now.Hour.ToString("00") + now.Minute.ToString("00") + now.Second.ToString("00");
                string f = Path.Combine(snapshotDir, "snap_" + stamp + ".json");
                File.WriteAllText(f, body, new UTF8Encoding(false));
                lastSnapshot = Environment.TickCount;
                PruneSnapshots(12);   // 只留最近 12 份
            }
            catch { }
        }

        static void PruneSnapshots(int keep)
        {
            try
            {
                var files = Directory.GetFiles(snapshotDir, "snap_*.json");
                Array.Sort(files);        // 文件名即时间序
                int extra = files.Length - keep;
                for (int i = 0; i < extra; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch { }
        }

        static void PruneBackups()
        {
            try
            {
                string cutoff = DayKey(DateTime.Now.AddDays(-60));
                foreach (var f in Directory.GetFiles(backupDir, "番茄待办_*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    string k = name.Replace("番茄待办_", "");
                    if (k.Length == 10 && k.CompareTo(cutoff) < 0)
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }

        // ---------------- config.js ----------------
        static void WriteConfigJs()
        {
            string js = "window.__PT_PORT=" + httpPort + ";window.__PT_TOKEN=\"" + token + "\";";
            File.WriteAllText(Path.Combine(dataDir, "config.js"), js, new UTF8Encoding(false));
        }

        static Stream ResourceStream(string name)
        {
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(name);
        }
        static void ExtractResource(string name, string dest)
        {
            using (var src = ResourceStream(name))
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                src.CopyTo(fs);
        }

        static void ActivateExistingInstance()
        {
            try
            {
                var pids = new HashSet<uint>();
                foreach (var p in Process.GetProcessesByName("msedge"))
                    pids.Add((uint)p.Id);
                string title;
                IntPtr h = Win32.FindWindow(pids, TITLE_MATCH, out title);
                if (h != IntPtr.Zero) Win32.ForceForeground(h);
            }
            catch { }
        }
    }
}
