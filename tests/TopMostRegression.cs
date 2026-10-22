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
    }

    static void SetField(object form, string name, object value)
    {
        typeof(TimelineForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
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
    static int Main()
    {
        NativeMethods.SetProcessDPIAware();
        Application.EnableVisualStyles();
        using (Probe form = new Probe())
        {
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
            CheckTop(form, "dock expand animation");
            Check(GetForegroundWindow() == foreground, "dock expand does not steal focus");
            SetField(form, "docked", false);
        }
        Console.WriteLine("Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
