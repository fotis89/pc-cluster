using System.Runtime.InteropServices;
using System.Windows.Automation;
using Cluster.Native;
using Cluster.Settings;

namespace Cluster.Ui;

/// <param name="Buttons">Extent of the taskbar buttons (Start, Search, Widgets, apps), physical px.</param>
/// <param name="OnScreen">False while an auto-hide taskbar is slid away.</param>
internal sealed record TaskbarInfo(RECT Bounds, TaskbarButtons? Buttons, MonitorInfo Monitor, bool OnScreen);

/// <param name="Left">Left edge of the leftmost button.</param>
/// <param name="GapStart">Start of the widest free stretch right of the first button (between buttons, or between the last one and the tray).</param>
/// <param name="GapEnd">End of that stretch; equal to <paramref name="GapStart"/> when there is none.</param>
internal sealed record TaskbarButtons(int Left, int GapStart, int GapEnd);

/// <summary>
/// Watches the taskbar of the chosen screen on a background thread and reports where its buttons start,
/// so the cluster can sit in the empty space beside them. Windows 11's taskbar buttons are XAML without
/// their own windows, so positions come from UI Automation (read-only). The UIA query only runs when the
/// taskbar's child windows change, and at least every 2 s.
/// </summary>
internal sealed class TaskbarProbe : IDisposable
{
    const int PeriodMs = 1000;
    static readonly TimeSpan ForceEvery = TimeSpan.FromSeconds(2);

    readonly ManualResetEventSlim stop = new();
    readonly Thread thread;
    volatile Target target;
    volatile bool force = true;

    sealed record Target(string? Device, int[]? Bounds);

    public event Action<TaskbarInfo?>? Changed;

    public TaskbarProbe(AppSettings settings)
    {
        target = new Target(settings.Screen, settings.ScreenBounds);
        thread = new Thread(Loop) { IsBackground = true, Name = "Cluster taskbar probe", Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.MTA); // UI Automation clients belong off the UI thread
    }

    public void Start() => thread.Start();

    public void Retarget(AppSettings settings)
    {
        target = new Target(settings.Screen, settings.ScreenBounds);
        force = true;
    }

    void Loop()
    {
        TaskbarInfo? last = null;
        bool first = true;
        long signature = 0;
        TaskbarButtons? buttons = null;
        var lastQuery = DateTime.MinValue;

        do
        {
            try
            {
                var t = target;
                var monitor = Monitors.Resolve(Monitors.All(), t.Device, t.Bounds);
                nint taskbar = FindTaskbar(monitor.Handle);
                TaskbarInfo? info = null;
                if (taskbar != 0 && User32.GetWindowRect(taskbar, out var bounds))
                {
                    long sig = Signature(taskbar, bounds);
                    var now = DateTime.UtcNow;
                    if (force || sig != signature || now - lastQuery >= ForceEvery)
                    {
                        force = false;
                        signature = sig;
                        lastQuery = now;
                        buttons = Measure(taskbar, bounds);
                    }
                    info = new TaskbarInfo(bounds, buttons, monitor, monitor.Bounds.Covers(bounds));
                }
                if (first || info != last)
                {
                    first = false;
                    last = info;
                    Changed?.Invoke(info);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Taskbar probe", ex);
            }
        } while (!stop.Wait(PeriodMs));
    }

    /// <summary>The primary taskbar (Shell_TrayWnd) or the secondary one on <paramref name="monitor"/>.</summary>
    internal static nint FindTaskbar(nint monitor)
    {
        nint primary = User32.FindWindowEx(0, 0, "Shell_TrayWnd", null);
        if (primary != 0 && User32.MonitorFromWindowNearest(primary) == monitor) return primary;
        nint h = 0;
        while ((h = User32.FindWindowEx(0, h, "Shell_SecondaryTrayWnd", null)) != 0)
            if (User32.MonitorFromWindowNearest(h) == monitor) return h;
        return 0;
    }

    /// <summary>Cheap change detector: taskbar rect plus the rects of its child windows.</summary>
    static long Signature(nint taskbar, RECT bounds)
    {
        long hash = HashCode.Combine(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        User32.EnumChildWindows(taskbar, (child, _) =>
        {
            if (User32.GetWindowRect(child, out var r))
                hash = hash * 31 + HashCode.Combine(r.Left, r.Top, r.Right, r.Bottom);
            return true;
        }, 0);
        return hash;
    }

    static readonly Condition Buttons = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

    internal static int? OccupiedLeft(nint taskbar, RECT bounds) => Measure(taskbar, bounds)?.Left;

    // The notification area is a window ("TrayNotifyWnd"; "ClockButton" on secondary taskbars) around its XAML buttons.
    static readonly Condition TrayArea = new OrCondition(
        new PropertyCondition(AutomationElement.ClassNameProperty, "TrayNotifyWnd"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "ClockButton"));

    internal static TaskbarButtons? Measure(nint taskbar, RECT bounds)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            int? trayLeft = null;
            var tray = root.FindFirst(TreeScope.Descendants, TrayArea);
            if (tray is not null)
            {
                var t = tray.Current.BoundingRectangle;
                if (!t.IsEmpty && t.Left > bounds.Left && t.Left < bounds.Right) trayLeft = (int)Math.Round(t.Left);
            }

            var rects = new List<(int Left, int Right)>();
            foreach (AutomationElement button in root.FindAll(TreeScope.Descendants, Buttons))
            {
                var r = button.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Left < bounds.Left - 1 || r.Right > bounds.Right + 1) continue;
                rects.Add(((int)Math.Round(r.Left), (int)Math.Round(r.Right)));
            }
            if (rects.Count == 0) return null;
            var (gapStart, gapEnd) = Placement.LargestGap(rects, trayLeft);
            return new TaskbarButtons(rects.Min(r => r.Left), gapStart, gapEnd);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        stop.Set();
        if (thread.ThreadState != ThreadState.Unstarted) thread.Join(2000);
        stop.Dispose();
    }
}
