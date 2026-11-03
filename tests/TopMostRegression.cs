// Windows native-window regression tests. Compile alongside Main.cs with /main:TopMostRegression.
using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DeepSeekTimeline;

static class TopMostRegression
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after,
        int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
    const uint NoMoveSizeActivate = 0x0001 | 0x0002 | 0x0010;
    static int failures;

    class Probe : TimelineForm
    {
        // Exercise the real handle lifecycle without activating a test window or saving test positions.
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnFormClosing(FormClosingEventArgs e) { }
        public void Recreate() { RecreateHandle(); }
        public Bitmap Snapshot()
        {
            var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
                base.OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, ClientSize)));
            return bitmap;
        }
    }

    static void SetField(object form, string name, object value)
    {
        typeof(TimelineForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
    }

    static object Field(object form, string name)
    {
        return typeof(TimelineForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    static bool Hidden(Probe form)
    {
        return (bool)typeof(TimelineForm).GetMethod("ContentHidden", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
    }

    static void Check(bool ok, string label)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
        if (!ok) failures++;
    }

    static void CheckTop(Probe form, string label)
    {
        bool native = (GetWindowLong(form.Handle, -20) & 8) != 0;
        Check(native, label + " (managed=" + form.TopMost + ", native=" + native + ")");
    }

    static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        for (IntPtr h = GetWindow(upper, 2); h != IntPtr.Zero; h = GetWindow(h, 2))
            if (h == lower) return true;
        return false;
    }

    static void DockTick(Probe form)
    {
        typeof(TimelineForm).GetMethod("DockTick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, null);
    }

    [STAThread]
    static int Main(string[] args)
    {
        NativeMethods.SetProcessDPIAware();
        Application.EnableVisualStyles();
        using (Probe form = new Probe())
        {
            // Exercise a truly invisible content area, including negative monitor origins.
            Rectangle top = Screen.PrimaryScreen.Bounds;
            foreach (Screen screen in Screen.AllScreens)
                if (screen.Bounds.Top < top.Top) top = screen.Bounds;
            SetField(form, "monRect", top);
            SetField(form, "docked", true);
            SetField(form, "expanded", false);
            form.Location = new Point(top.Left + 20, top.Top - form.Height + 5);
            foreach (string name in new[] { "fontBig", "fontSub", "fontTick", "fontPrice" })
                Check(Field(form, name) == null, "cold hidden font deferred: " + name);
            Check(Hidden(form), "content outside all monitors can skip text rendering");
            using (Bitmap hidden = form.Snapshot())
            {
                Check(Field(form, "fontBig") == null, "hidden paint does not initialize fonts");
                if (args.Length > 0 && args[0] == "--close-hidden")
                {
                    form.Show();
                    Application.DoEvents();
                    form.Close();
                    Check(Field(form, "fontBig") == null, "never-expanded close keeps fonts uninitialized");
                    Console.WriteLine("Failures: " + failures);
                    return failures == 0 ? 0 : 1;
                }
                SetField(form, "expanded", true);
                form.Top = top.Top;
                using (Bitmap full = form.Snapshot())
                {
                    foreach (string name in new[] { "fontBig", "fontSub", "fontTick", "fontPrice" })
                        Check(Field(form, name) != null, "first expanded paint initializes font: " + name);
                    bool same = true;
                    for (int y = form.Height - 5; y < form.Height; y++)
                        for (int x = 0; x < form.Width; x++)
                            if (hidden.GetPixel(x, y) != full.GetPixel(x, y)) same = false;
                    Check(same, "visible 5px strip matches complete rendering pixel-for-pixel");
                }
                object font = Field(form, "fontBig");
                using (Bitmap again = form.Snapshot())
                    Check(object.ReferenceEquals(font, Field(form, "fontBig")), "expanded repaints reuse font instances");
            }
            // Real screen geometry guard: content touching another display is not hidden.
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle bounds = screen.Bounds;
                SetField(form, "monRect", new Rectangle(bounds.Left, bounds.Top + 100, bounds.Width, bounds.Height));
                SetField(form, "expanded", false);
                form.Location = new Point(bounds.Left + 20, bounds.Top - 17);
                Check(!Hidden(form), "visible content on another monitor is not skipped: " + bounds.Top);
            }
            Check(((Timer)Field(form, "dockTimer")).Interval == 100, "idle dock detection starts at 100ms");
            SetField(form, "docked", false);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            // Keep the probe out of the way; it never writes the user's ini file.
            form.Location = new Point(wa.Left - form.Width + 10, wa.Bottom - form.Height);
            CheckTop(form, "handle creation");
            form.Show();
            Application.DoEvents();
            CheckTop(form, "initial Show");

            IntPtr foreground = GetForegroundWindow();
            Rectangle originalBounds = form.Bounds;
            Check(SetWindowPos(form.Handle, new IntPtr(-2), 0, 0, 0, 0, NoMoveSizeActivate),
                "native demotion request succeeds");
            Application.DoEvents();
            CheckTop(form, "native demotion cannot leave the window unpinned");
            Check(GetForegroundWindow() == foreground, "topmost maintenance does not steal focus");
            Check(form.Bounds == originalBounds, "topmost repair preserves position and size");
            Check(SetWindowPos(form.Handle, new IntPtr(1), 0, 0, 0, 0, NoMoveSizeActivate),
                "send-to-bottom request succeeds");
            Application.DoEvents();
            CheckTop(form, "send-to-bottom cannot leave the window unpinned");
            SetWindowLong(form.Handle, -20, GetWindowLong(form.Handle, -20) & ~8);
            Application.DoEvents();
            CheckTop(form, "direct native style change cannot leave the window unpinned");
            Check(GetForegroundWindow() == foreground, "style repair does not steal focus");
            form.TopMost = true;
            CheckTop(form, "managed TopMost=true agrees with native state");

            // A separate normal window can become foreground without demoting the timeline.
            using (Form normal = new Form())
            {
                normal.StartPosition = FormStartPosition.Manual;
                normal.Location = form.Location;
                normal.Show();
                normal.Activate();
                Application.DoEvents();
                CheckTop(form, "another foreground window");
                normal.Hide();
            }

            using (Form otherTopmost = new Form())
            {
                otherTopmost.TopMost = true;
                otherTopmost.StartPosition = FormStartPosition.Manual;
                otherTopmost.Location = form.Location;
                otherTopmost.Show();
                Application.DoEvents();
                // Walking downward from the other topmost window must reach the timeline.
                bool aboveBefore = IsAbove(otherTopmost.Handle, form.Handle);
                Check(aboveBefore, "another topmost window can be above the timeline");
                SetWindowPos(form.Handle, IntPtr.Zero, form.Left + 1, form.Top, 0, 0,
                    0x0001 | 0x0004 | 0x0010);
                Application.DoEvents();
                Check(IsAbove(otherTopmost.Handle, form.Handle), "ordinary movement does not leapfrog other topmost windows");
                otherTopmost.Hide();
            }

            form.Recreate();
            Application.DoEvents();
            CheckTop(form, "handle recreation");
            form.Hide();
            form.Show();
            Application.DoEvents();
            CheckTop(form, "hide and show");

            // Exercise both branches of the real animation without moving the user's pointer.
            Point pointer = Cursor.Position;
            Rectangle monitor = Screen.FromControl(form).Bounds;
            SetField(form, "monRect", monitor);
            SetField(form, "docked", true);
            SetField(form, "expanded", true);
            SetField(form, "dragging", false);
            form.Location = new Point(pointer.X + form.Width + 200, monitor.Top + 20);
            int before = form.Top;
            foreground = GetForegroundWindow();
            DockTick(form);
            Check(form.Top < before, "dock retract animation moves upward");
            Check(((Timer)Field(form, "dockTimer")).Interval == 30, "retract animation uses 30ms frames");
            for (int i = 0; i < 30; i++) DockTick(form);
            Check(form.Top == monitor.Top - form.Height + 5, "retract reaches exact hidden position");
            Check(((Timer)Field(form, "dockTimer")).Interval == 100, "retracted window returns to 100ms detection");
            CheckTop(form, "dock retract animation");
            Check(GetForegroundWindow() == foreground, "dock retract does not steal focus");

            // Simulate a monitor whose top edge is at the pointer, ensuring hover expansion.
            monitor = new Rectangle(pointer.X - 100, pointer.Y, 1920, 1080);
            SetField(form, "monRect", monitor);
            SetField(form, "expanded", false);
            form.Location = new Point(pointer.X - 100, monitor.Top - form.Height + 5);
            before = form.Top;
            DockTick(form);
            Check(form.Top > before, "dock expand animation moves downward");
            Check(((Timer)Field(form, "dockTimer")).Interval == 30, "expand animation uses 30ms frames");
            for (int i = 0; i < 30; i++) DockTick(form);
            Check(form.Top == monitor.Top, "expand reaches exact monitor top");
            Check(((Timer)Field(form, "dockTimer")).Interval == 100, "expanded stationary window uses 100ms detection");
            CheckTop(form, "dock expand animation");
            Check(GetForegroundWindow() == foreground, "dock expand does not steal focus");

            // A saved retracted position must return to the screen it was docked to, also when
            // the retracted window rect spans two vertically adjacent monitors.
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle bounds = screen.Bounds;
                Probe saved = new Probe();
                SetField(saved, "docked", true);
                saved.Location = new Point(bounds.Left + 10, bounds.Top - saved.Height + 5);
                IntPtr savedHandle = saved.Handle;
                typeof(TimelineForm).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(saved, new object[] { EventArgs.Empty });
                Check(saved.Top == bounds.Top - saved.Height + 5,
                    "saved retracted position stays on its own screen: top=" + bounds.Top);
                GC.KeepAlive(savedHandle);
            }
            SetField(form, "docked", false);
        }
        Console.WriteLine("Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
