// DeepSeekTimeline — DeepSeek 峰谷定价 24 小时时间线浮窗 (WinForms, C# 5)
// 编译: build.bat (使用 Windows 自带 csc.exe, 无需安装任何 SDK)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepSeekTimeline
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new TimelineForm());
        }
    }

    static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int L, T, R, B; }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int L, R, T, B; }

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS m);
    }

    class TimelineForm : Form
    {
        // ============ 配置区: 官方调价 / 调时段后只需改这里 ============
        // 峰时时段 [起,止) 北京时间, 其余为谷时
        static readonly int[][] PeakRanges = new int[][]
        {
            new int[] { 9, 12 },
            new int[] { 14, 18 }
        };
        // 百万 tokens 单价(元) [0]=谷 [1]=峰
        static readonly decimal[] PriceFlashIn  = new decimal[] { 1.5m, 3.0m };
        static readonly decimal[] PriceFlashOut = new decimal[] { 4.5m, 9.0m };
        static readonly decimal[] PriceProIn    = new decimal[] { 4.5m, 9.0m };
        static readonly decimal[] PriceProOut   = new decimal[] { 13.5m, 27.0m };
        // ==============================================================

        const int W = 520, H = 122;
        const int BandLeft = 20, BandTop = 52, BandRight = W - 20, BandBottom = 68;
        const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDBLCLK = 0xA3, HTCLIENT = 1, HTCAPTION = 2;
        const int WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232;
        const int Peek = 5; // 吸附隐藏时顶部保留的提示条高度(px)

        // 淡色系
        static readonly Color Bg        = Color.FromArgb(247, 247, 248);
        static readonly Color Border    = Color.FromArgb(224, 224, 229);
        static readonly Color PeakColor = Color.FromArgb(233, 163, 154);
        static readonly Color OffColor  = Color.FromArgb(139, 190, 174);
        static readonly Color Accent    = Color.FromArgb(198, 111, 100);
        static readonly Color AccentOff = Color.FromArgb(88, 143, 124);
        static readonly Color TextMain  = Color.FromArgb(40, 40, 46);
        static readonly Color TextSub   = Color.FromArgb(110, 112, 119);
        static readonly Color TextFaint = Color.FromArgb(154, 156, 163);
        static readonly Color Pointer   = Color.FromArgb(40, 40, 46);

        readonly bool[] peakHour = new bool[24];
        readonly Font fontBig, fontSub, fontTick;
        readonly Timer timer, dockTimer;
        readonly Rectangle closeRect = new Rectangle(W - 24, 8, 14, 14);
        bool hoverClose, docked, expanded, dragging;
        Rectangle monRect;

        // ---- 性能缓存: 所有颜色/几何均静态, GDI 对象只创建一次, 复用至进程结束 ----
        // TransHours 由 PeakRanges 派生并排序, 官方调时段后无需同步第二处
        static readonly int[] TransHours = BuildTransHours();
        static readonly SolidBrush
            BrPeak = new SolidBrush(PeakColor),
            BrOff = new SolidBrush(OffColor),
            BrDim = new SolidBrush(Color.FromArgb(165, 247, 247, 248)),
            BrMain = new SolidBrush(TextMain),
            BrSub = new SolidBrush(TextSub),
            BrFaint = new SolidBrush(TextFaint),
            BrAccent = new SolidBrush(Accent),
            BrAccentOff = new SolidBrush(AccentOff),
            BrDot = new SolidBrush(Pointer);
        static readonly Pen
            PenPointer = new Pen(Pointer, 1.4f),
            PenBorder = new Pen(Border),
            PenCloseDim = new Pen(Color.FromArgb(165, 167, 173), 1.6f),
            PenCloseHot = new Pen(Color.FromArgb(200, 90, 80), 1.6f);
        static readonly GraphicsPath BandPath = RoundedRect(new RectangleF(BandLeft,
            BandTop, BandRight - BandLeft, BandBottom - BandTop), 5f);

        static int[] BuildTransHours()
        {
            List<int> ts = new List<int>();
            foreach (int[] r in PeakRanges) { ts.Add(r[0]); ts.Add(r[1]); }
            ts.Sort();
            return ts.ToArray();
        }

        public TimelineForm()
        {
            foreach (int[] r in PeakRanges)
                for (int h = r[0]; h < r[1]; h++) peakHour[h] = true;

            fontBig  = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            fontSub  = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
            fontTick = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Regular);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            ClientSize = new Size(W, H);
            Opacity = 0.80;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            if (!LoadPosition()) Location = DefaultPosition();

            timer = new Timer();
            // 分钟对齐重绘: 画面均为分钟粒度, 倒计时误差 <1s
            timer.Tick += delegate
            {
                SyncTimerToMinute();
                // 完全收起时可见区只有 5px 静态边条, 跳过无效重绘
                if (docked && !expanded && Top <= monRect.Top - H + Peek) return;
                Invalidate();
            };
            SyncTimerToMinute();
            timer.Start();

            dockTimer = new Timer();
            dockTimer.Interval = 30;
            dockTimer.Tick += delegate { DockTick(); };
            // 仅吸附状态才需要轮询鼠标, 未吸附时保持停止 (见 WndProc/OnLoad)
        }

        // 不抢焦点 + 不出现在 Alt-Tab (悬浮组件的标准样式)
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            monRect = Screen.FromControl(this).Bounds;
            if (docked)
            {
                Location = new Point(Location.X, monRect.Top - H + Peek);
                expanded = false;
                dockTimer.Start();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;
            NativeMethods.DwmSetWindowAttribute(Handle, 33, ref round, 4);
            NativeMethods.MARGINS mg = new NativeMethods.MARGINS { L = 1, R = 1, T = 1, B = 1 };
            NativeMethods.DwmExtendFrameIntoClientArea(Handle, ref mg);
        }

        // 距下一整分钟 +0.1s 过冲
        void SyncTimerToMinute()
        {
            DateTime n = DateTime.Now;
            timer.Interval = 60100 - (n.Second * 1000 + n.Millisecond);
        }

        DateTime NextTransition(DateTime now, out bool toPeak)
        {
            foreach (int hh in TransHours)
            {
                DateTime t = new DateTime(now.Year, now.Month, now.Day, hh, 0, 0);
                if (t > now) { toPeak = peakHour[hh]; return t; }
            }
            DateTime d = now.Date.AddDays(1);
            toPeak = peakHour[TransHours[0]];
            return new DateTime(d.Year, d.Month, d.Day, TransHours[0], 0, 0);
        }

        Point DefaultPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - W - 24, wa.Bottom - H - 24);
        }

        string CfgPath()
        {
            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "DeepSeekTimeline.ini");
        }

        bool LoadPosition()
        {
            try
            {
                if (!File.Exists(CfgPath())) return false;
                string[] p = File.ReadAllText(CfgPath()).Split(',');
                int x = int.Parse(p[0], CultureInfo.InvariantCulture);
                int y = int.Parse(p[1], CultureInfo.InvariantCulture);
                if (p.Length > 2 && p[2].Trim() == "1") docked = true;
                Rectangle vs = SystemInformation.VirtualScreen;
                if (docked)
                {
                    if (x < vs.Left) x = vs.Left;
                    if (x > vs.Right - W) x = vs.Right - W;
                    Location = new Point(x, 0); // Y 由 OnLoad 按吸附位修正
                    return true;
                }
                if (x < vs.Left - 60 || x > vs.Right - 60 || y < vs.Top - 30 || y > vs.Bottom - 30)
                    return false;
                Location = new Point(x, y);
                return true;
            }
            catch { return false; }
        }

        void SavePosition()
        {
            try
            {
                File.WriteAllText(CfgPath(), string.Format(
                    CultureInfo.InvariantCulture, "{0},{1},{2}", Left, Top, docked ? 1 : 0));
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ENTERSIZEMOVE)
            {
                dragging = true;
                base.WndProc(ref m);
                return;
            }
            if (m.Msg == WM_EXITSIZEMOVE)
            {
                // 拖拽结束: 判定 进入吸附 / 保持吸附 / 脱离吸附
                dragging = false;
                monRect = Screen.FromControl(this).Bounds;
                if (docked)
                {
                    if (Top > monRect.Top + 30) { docked = false; dockTimer.Stop(); } // 下拉脱离
                }
                else if (Top <= monRect.Top + 8)
                {
                    docked = true;                              // 拖到顶 = 吸附
                    expanded = true;
                    Invalidate();
                    dockTimer.Start();
                }
                SavePosition();
                base.WndProc(ref m);
                return;
            }
            if (m.Msg == WM_NCLBUTTONDBLCLK) // 吞掉标题栏双击, 防止系统最大化全屏
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                int lp = (int)m.LParam;
                int sx = lp & 0xFFFF; if (sx >= 0x8000) sx -= 0x10000;
                int sy = (lp >> 16) & 0xFFFF; if (sy >= 0x8000) sy -= 0x10000;
                Point pt = PointToClient(new Point(sx, sy));
                m.Result = (IntPtr)(closeRect.Contains(pt) ? HTCLIENT : HTCAPTION);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (closeRect.Contains(e.Location)) Close();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = closeRect.Contains(e.Location);
            if (h != hoverClose)
            {
                hoverClose = h;
                Cursor = h ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverClose) { hoverClose = false; Cursor = Cursors.Default; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePosition();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (IDisposable d in new IDisposable[] { timer, dockTimer,
                fontBig, fontSub, fontTick, BrPeak, BrOff, BrDim, BrMain, BrSub,
                BrFaint, BrAccent, BrAccentOff, BrDot, PenPointer, PenBorder,
                PenCloseDim, PenCloseHot, BandPath }) d.Dispose();
            base.OnFormClosed(e);
        }

        static float Wd(Graphics g, string s, Font f)
        {
            return g.MeasureString(s, f).Width;
        }

        // 吸附模式核心: 鼠标感应 + 缓出滑动动画
        void DockTick()
        {
            if (!docked || dragging) return;
            Point mp = Cursor.Position;
            Rectangle wr = Bounds;
            bool nearTop = mp.X >= wr.Left - 80 && mp.X <= wr.Right + 80
                        && mp.Y >= monRect.Top - 4 && mp.Y <= monRect.Top + 6;
            bool inside = mp.X >= wr.Left - 20 && mp.X <= wr.Right + 20
                       && mp.Y >= wr.Top - 20 && mp.Y <= wr.Bottom + 20;
            if (nearTop || inside)
            {
                if (!expanded && !ForegroundFullscreen())
                {
                    expanded = true;
                    Invalidate(); // 展开瞬间立即按最新时间重绘, 杜绝旧数据
                }
            }
            else if (expanded)
            {
                expanded = false; // 鼠标离开立即收起, 与展开同样零延迟
            }
            int target = expanded ? monRect.Top : monRect.Top - H + Peek;
            if (Top != target)
            {
                int dy = target - Top;
                int step = dy / 3;
                if (step == 0) step = dy > 0 ? 1 : -1;
                NativeMethods.SetWindowPos(Handle, IntPtr.Zero, Left, Top + step, 0, 0,
                    0x0001 | 0x0004 | 0x0010); // NOSIZE | NOZORDER | NOACTIVATE
            }
        }

        // 前台程序全屏(视频/游戏)时不弹出, 防干扰
        bool ForegroundFullscreen()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle) return false;
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(fg, out r)) return false;
            return r.L <= monRect.Left && r.T <= monRect.Top
                && r.R >= monRect.Right && r.B >= monRect.Bottom;
        }

        static GraphicsPath RoundedRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        static string Fmt(decimal v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Bg);

            DateTime now = DateTime.Now;
            bool peak = peakHour[now.Hour];
            int idx = peak ? 1 : 0;
            float bandW = BandRight - BandLeft;
            double cur = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;

            // ---- 第一行: 左侧状态 (实测宽度 + 固定 6px 间距) ----
            string s1 = peak ? "峰时" : "谷时";
            string s2 = peak ? "全价" : "5 折";
            g.FillEllipse(peak ? BrAccent : BrAccentOff, 22, 21, 9, 9);
            g.DrawString(s1, fontBig, BrMain, 40, 13);
            g.DrawString(s2, fontSub, BrSub, 40 + Wd(g, s1, fontBig) + 6, 19);

            // 右侧倒计时: 数字右对齐, 标签再向左排
            bool toPeak;
            DateTime nt = NextTransition(now, out toPeak);
            TimeSpan ts = nt - now;
            string cv = string.Format("{0}:{1:00}", (int)ts.TotalHours, ts.Minutes);
            string cl = string.Format("距转入{0}", toPeak ? "峰" : "谷");
            float vx = W - 36 - Wd(g, cv, fontBig);
            g.DrawString(cv, fontBig, BrMain, vx, 13);
            g.DrawString(cl, fontSub, BrSub, vx - 8 - Wd(g, cl, fontSub), 19);

            // ---- 色带 (已流逝时段蒙白, 边界精确到当前时刻与指针对齐) ----
            float xNow = BandLeft + bandW * (float)(cur / 24.0);
            g.SetClip(BandPath);
            for (int h = 0; h < 24; h++)
            {
                float x1 = BandLeft + bandW * h / 24f;
                float x2 = BandLeft + bandW * (h + 1) / 24f;
                g.FillRectangle(peakHour[h] ? BrPeak : BrOff,
                    x1, BandTop, x2 - x1 + 1f, BandBottom - BandTop);
            }
            g.FillRectangle(BrDim, BandLeft, BandTop, xNow - BandLeft, BandBottom - BandTop);
            g.ResetClip();

            // ---- 关键节点标注: 仅峰谷转换时刻 ----
            foreach (int hh in TransHours)
            {
                float x = BandLeft + bandW * hh / 24f;
                string t = hh.ToString(CultureInfo.InvariantCulture);
                g.DrawString(t, fontTick, BrFaint, x - Wd(g, t, fontTick) / 2f, BandBottom + 4f);
            }

            // ---- 当前时刻指针 (深色细线 + 圆点) ----
            g.DrawLine(PenPointer, xNow, BandTop - 3f, xNow, BandBottom + 3f);
            g.FillEllipse(BrDot, xNow - 3f, BandTop - 10f, 6f, 6f);

            // ---- 价格行: Flash(左) / Pro(右) ----
            float by = 96;
            string fl = string.Format(CultureInfo.InvariantCulture,
                "Flash 入 {0} · 出 {1}", Fmt(PriceFlashIn[idx]), Fmt(PriceFlashOut[idx]));
            string pr = string.Format(CultureInfo.InvariantCulture,
                "Pro 入 {0} · 出 {1}", Fmt(PriceProIn[idx]), Fmt(PriceProOut[idx]));
            g.DrawString(fl, fontSub, BrSub, 20, by);
            g.DrawString(pr, fontSub, BrSub, W - 20 - Wd(g, pr, fontSub), by);

            // ---- 关闭按钮 ----
            Pen cp = hoverClose ? PenCloseHot : PenCloseDim;
            g.DrawLine(cp, closeRect.Left + 3, closeRect.Top + 4,
                closeRect.Right - 3, closeRect.Bottom - 3);
            g.DrawLine(cp, closeRect.Right - 3, closeRect.Top + 4,
                closeRect.Left + 3, closeRect.Bottom - 3);

            // ---- 细边框 ----
            g.DrawRectangle(PenBorder, 0.5f, 0.5f, W - 1f, H - 1f);
        }
    }
}
